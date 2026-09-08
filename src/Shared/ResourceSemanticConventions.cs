// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Resources;

internal static class ResourceSemanticConventions
{
    public const string AttributeServiceName = "service.name";
    public const string AttributeServiceNamespace = "service.namespace";
    public const string AttributeServiceInstance = "service.instance.id";
    public const string AttributeServiceVersion = "service.version";

    public const string AttributeTelemetrySdkName = "telemetry.sdk.name";
    public const string AttributeTelemetrySdkLanguage = "telemetry.sdk.language";
    public const string AttributeTelemetrySdkVersion = "telemetry.sdk.version";

    // Entity types from the entity registry:
    // https://github.com/open-telemetry/semantic-conventions/tree/main/docs/registry/entities
    //
    // Note: the registry actually models "service", "service namespace", and "service instance" as
    // three distinct entities (each with their own identifying attribute). This proof-of-concept
    // collapses them into a single "service" entity, using service.name as the sole identifying
    // attribute and folding service.namespace/service.instance.id/service.version into its
    // descriptive attributes, to keep the API surface simple to demonstrate.
    public const string EntityTypeService = "service";
    public const string EntityTypeTelemetrySdk = "telemetry.sdk";
}
