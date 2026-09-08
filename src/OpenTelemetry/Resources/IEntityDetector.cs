// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Resources;

/// <summary>
/// An interface for entity detectors.
/// </summary>
/// <remarks>
/// <para experimental-warning="true"><b>WARNING</b>: This is an experimental API which might change or be removed in the future. Use at your own risk.</para>
/// </remarks>
[Experimental(DiagnosticDefinitions.EntitiesExperimentalApi, UrlFormat = DiagnosticDefinitions.ExperimentalApiUrlFormat)]
public interface IEntityDetector
{
    /// <summary>
    /// Called to detect the <see cref="Entity"/> instances contributed by this detector.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> of zero or more <see cref="Entity"/> instances.</returns>
    IEnumerable<Entity> Detect();
}
