﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Registration;
using Task = System.Threading.Tasks.Task;

namespace SandboxImporter
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            EnsureArg.IsNotNull(args, nameof(args));

            await Task.CompletedTask;

            var host = WebHost.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostContext, configApp) =>
                {
                    configApp.SetBasePath(Directory.GetCurrentDirectory());
                    configApp.AddJsonFile("importerappsettings.json", optional: false);
                    configApp.AddJsonFile(
                        $"importerappsettings.{hostContext.HostingEnvironment.EnvironmentName}.json",
                        optional: true);
                    configApp.AddEnvironmentVariables(prefix: "PREFIX_");
                })
                .ConfigureServices((context, collection) =>
                {
                    IFhirServerBuilder fhirServerBuilder = collection.AddFhirServer(context.Configuration);

                    fhirServerBuilder.Services.Add(provider =>
                        {
                            var config = new SqlServerDataStoreConfiguration();
                            provider.GetService<IConfiguration>().GetSection("SqlServer").Bind(config);

                            return config;
                        })
                        .Singleton()
                        .AsSelf();

                    fhirServerBuilder.Services.Add<SqlServerDataStore>()
                        .Singleton()
                        .AsSelf()
                        .AsImplementedInterfaces();

                    collection.Add<UrlHelperFactory>()
                        .Singleton()
                        .ReplaceService<IUrlHelperFactory>();
                })
                .Configure(builder => { })
                .Build();

            foreach (var startable in host.Services.GetRequiredService<IEnumerable<IStartable>>())
            {
                startable.Start();
            }

            using (var serviceScope = host.Services.CreateScope())
            {
                serviceScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
                serviceScope.ServiceProvider.GetRequiredService<IFhirRequestContextAccessor>().FhirRequestContext = new FhirRequestContext("PUT", "https://x", "https://y", new Coding("a", "b"), "w", ImmutableDictionary<string, StringValues>.Empty, ImmutableDictionary<string, StringValues>.Empty);

                var mediator = serviceScope.ServiceProvider.GetRequiredService<IMediator>();

                var fhirJsonParser = new FhirJsonParser();
                var overallSw = Stopwatch.StartNew();

                int patientCount = 0;
                int resourceCount = 0;
                var parseNdJson = new TransformManyBlock<string, Resource>(
                    async ndJsonPath => (await File.ReadAllLinesAsync(ndJsonPath)).Select(fhirJsonParser.Parse<Resource>),
                    new ExecutionDataflowBlockOptions { BoundedCapacity = 10, MaxDegreeOfParallelism = Environment.ProcessorCount });
                var upsertBlock = new ActionBlock<Resource>(
                    async resource =>
                    {
                        Interlocked.Increment(ref resourceCount);
                        try
                        {
                            await mediator.UpsertResourceAsync(resource);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            throw;
                        }
                    },
                    new ExecutionDataflowBlockOptions { BoundedCapacity = 1000, MaxDegreeOfParallelism = Environment.ProcessorCount });

                parseNdJson.LinkTo(upsertBlock, new DataflowLinkOptions() { PropagateCompletion = true });

                foreach (var ndJson in Directory.EnumerateFiles(@"E:\repos\synthea\output\fhir\ndjson"))
                {
                    patientCount++;
                    await parseNdJson.SendAsync(ndJson);
                }

                parseNdJson.Complete();
                await upsertBlock.Completion;

                Console.WriteLine($"All done. {patientCount} patients ({resourceCount} resources) uploaded in {overallSw.Elapsed}. {patientCount / overallSw.Elapsed.TotalHours} patients per hour");
                Console.ReadKey();
            }
        }
    }
}
