// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Resources;

/// <summary>
/// Represents an entity that participates in a <see cref="Resource"/>, as described by the
/// <a href="https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/entities/README.md">
/// entities</a> specification.
/// </summary>
/// <remarks>
/// <para experimental-warning="true"><b>WARNING</b>: This is an experimental API which might change or be removed in the future. Use at your own risk.</para>
/// An <see cref="Entity"/> is identified by its <see cref="IdentifyingAttributes"/>, which MUST
/// remain constant for the lifetime of the entity, and MAY carry additional
/// <see cref="DescriptiveAttributes"/> which can change over time. Both sets of attributes are
/// expected to also be present in the <see cref="Resource.Attributes"/> of the <see cref="Resource"/>
/// that the entity is associated with; see <see cref="ResourceBuilder.AddEntityDetector(IEntityDetector)"/>.
/// </remarks>
[Experimental(DiagnosticDefinitions.EntitiesExperimentalApi, UrlFormat = DiagnosticDefinitions.ExperimentalApiUrlFormat)]
public sealed class Entity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Entity"/> class.
    /// </summary>
    /// <param name="type">The type of the entity, e.g., <c>"service"</c>.</param>
    /// <param name="identifyingAttributes">
    /// The attributes that identify the entity. Must not change during the lifetime of the entity
    /// and must contain at least one attribute.
    /// </param>
    /// <param name="descriptiveAttributes">
    /// Optional, non-identifying attributes that describe the entity and may change over time.
    /// </param>
    /// <param name="schemaUrl">
    /// The Schema URL that applies to the entity, or <see langword="null"/> if the entity has no Schema URL.
    /// </param>
#pragma warning disable CA1054 // Change the type of parameter from 'string' to 'System.Uri'
    public Entity(
        string type,
        IEnumerable<KeyValuePair<string, object>> identifyingAttributes,
        IEnumerable<KeyValuePair<string, object>>? descriptiveAttributes = null,
        string? schemaUrl = null)
#pragma warning restore CA1054 // Change the type of parameter from 'string' to 'System.Uri'
    {
        Guard.ThrowIfNullOrEmpty(type);
        Guard.ThrowIfNull(identifyingAttributes);

        var identifying = identifyingAttributes as IReadOnlyList<KeyValuePair<string, object>> ?? [.. identifyingAttributes];
        if (identifying.Count == 0)
        {
            throw new ArgumentException("An entity must declare at least one identifying attribute.", nameof(identifyingAttributes));
        }

        this.Type = type;
        this.IdentifyingAttributes = identifying;
        this.DescriptiveAttributes = descriptiveAttributes as IReadOnlyList<KeyValuePair<string, object>> ?? descriptiveAttributes?.ToList() ?? [];
        this.SchemaUrl = string.IsNullOrEmpty(schemaUrl) ? null : schemaUrl;
    }

    /// <summary>
    /// Gets the type of the entity, e.g., <c>"service"</c>.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the attributes that identify the entity. Does not change during the lifetime of the entity.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object>> IdentifyingAttributes { get; }

    /// <summary>
    /// Gets non-identifying attributes that describe the entity. May change over the lifetime of the entity.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object>> DescriptiveAttributes { get; }

#pragma warning disable CA1056 // Change the type of property from 'string' to 'System.Uri'
    /// <summary>
    /// Gets the Schema URL that applies to the entity, or <see langword="null"/> if the entity has no Schema URL.
    /// </summary>
    public string? SchemaUrl { get; }
#pragma warning restore CA1056 // Change the type of property from 'string' to 'System.Uri'
}
