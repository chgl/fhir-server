﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression that indicates the search parameter should be missing.
    /// </summary>
    public class MissingSearchParameterExpression : SearchParameterExpressionBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MissingSearchParameterExpression"/> class.
        /// </summary>
        /// <param name="parameterName">THe search parameter name</param>
        /// <param name="isMissing">A flag indicating whether the parameter should be missing or not.</param>
        public MissingSearchParameterExpression(string parameterName, bool isMissing)
            : base(parameterName)
        {
            IsMissing = isMissing;
        }

        /// <summary>
        /// Gets a value indicating whether the parameter should be missing or not.
        /// </summary>
        public bool IsMissing { get; }

        protected internal override void AcceptVisitor(IExpressionVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }
    }
}