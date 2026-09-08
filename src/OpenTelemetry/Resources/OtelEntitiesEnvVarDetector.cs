// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Resources;

/// <summary>
/// Detects entities declared via the <c>OTEL_ENTITIES</c> environment variable, per the
/// <a href="https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/entities/entity-propagation.md">
/// entity propagation</a> specification.
/// </summary>
/// <remarks>
/// The parser below implements the grammar described in the specification but has not been hardened
/// against every edge case (e.g. it does not attempt to validate percent-encoding beyond what
/// <see cref="WebUtility.UrlDecode(string)"/> already does).
/// </remarks>
internal sealed partial class OtelEntitiesEnvVarDetector : IEntityDetector
{
    public const string EnvVarKey = "OTEL_ENTITIES";

    // entity := type "{" id_attrs "}" ( "[" desc_attrs "]" )? ( "@" schema_url )?
    private const string EntityPatternRegex = @"^(?<type>[a-zA-Z][a-zA-Z0-9._\-]*)\{(?<id>[^{}]*)\}(?:\[(?<desc>[^\[\]]*)\])?(?:@(?<schema>[^;]*))?$";

#if !NET
    private static readonly Regex EntityPattern = new(EntityPatternRegex, RegexOptions.Compiled);
#endif

    private readonly IConfiguration configuration;

    public OtelEntitiesEnvVarDetector(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public IEnumerable<Entity> Detect()
    {
        if (!this.configuration.TryGetStringValue(EnvVarKey, out var envEntitiesValue) || string.IsNullOrWhiteSpace(envEntitiesValue))
        {
            yield break;
        }

        foreach (var rawEntity in envEntitiesValue.Split(';'))
        {
            var trimmedEntity = rawEntity.Trim();
            if (trimmedEntity.Length == 0)
            {
                // Empty entity definitions (e.g. from leading/trailing/consecutive ';') are ignored.
                continue;
            }

#if NET
            var match = EntityPattern().Match(trimmedEntity);
#else
            var match = EntityPattern.Match(trimmedEntity);
#endif

            if (!match.Success)
            {
                OpenTelemetrySdkEventSource.Log.InvalidArgument(nameof(OtelEntitiesEnvVarDetector), EnvVarKey, $"Could not parse entity definition '{trimmedEntity}'.");
                continue;
            }

            var identifyingAttributes = ParseAttributeList(match.Groups["id"].Value);
            if (identifyingAttributes.Count == 0)
            {
                OpenTelemetrySdkEventSource.Log.InvalidArgument(nameof(OtelEntitiesEnvVarDetector), EnvVarKey, $"Entity definition '{trimmedEntity}' does not declare any identifying attributes.");
                continue;
            }

            var descriptiveAttributes = match.Groups["desc"].Success ? ParseAttributeList(match.Groups["desc"].Value) : null;
            var schemaUrl = match.Groups["schema"].Success ? match.Groups["schema"].Value : null;

            yield return new Entity(match.Groups["type"].Value, identifyingAttributes, descriptiveAttributes, schemaUrl);
        }
    }

    private static List<KeyValuePair<string, object>> ParseAttributeList(string rawAttributes)
    {
        var attributes = new List<KeyValuePair<string, object>>();

        if (string.IsNullOrEmpty(rawAttributes))
        {
            return attributes;
        }

        foreach (var rawKeyValuePair in rawAttributes.Split(','))
        {
            var keyValuePair = rawKeyValuePair.Split(['='], 2);
            if (keyValuePair.Length != 2)
            {
                continue;
            }

            var value = WebUtility.UrlDecode(keyValuePair[1].Trim());
            attributes.Add(new KeyValuePair<string, object>(keyValuePair[0].Trim(), value));
        }

        return attributes;
    }

#if NET
    [GeneratedRegex(EntityPatternRegex)]
    private static partial Regex EntityPattern();
#endif
}
