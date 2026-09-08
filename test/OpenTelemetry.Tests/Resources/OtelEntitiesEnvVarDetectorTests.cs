// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using OpenTelemetry.Tests;

namespace OpenTelemetry.Resources.Tests;

[Collection(EnvVarsCollectionDefinition.Name)]
public sealed class OtelEntitiesEnvVarDetectorTests : IDisposable
{
    public OtelEntitiesEnvVarDetectorTests()
    {
        Environment.SetEnvironmentVariable(OtelEntitiesEnvVarDetector.EnvVarKey, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OtelEntitiesEnvVarDetector.EnvVarKey, null);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void OtelEntitiesEnvVar_EnvVarKey()
        => Assert.Equal("OTEL_ENTITIES", OtelEntitiesEnvVarDetector.EnvVarKey);

    [Fact]
    public void OtelEntitiesEnvVar_Null()
    {
        var entities = Detect();

        Assert.Empty(entities);
    }

    [Fact]
    public void OtelEntitiesEnvVar_MinimalEntity()
    {
        var entities = Detect("service{service.name=minimal-app}");

        var entity = Assert.Single(entities);
        Assert.Equal("service", entity.Type);
        Assert.Equal(new KeyValuePair<string, object>("service.name", "minimal-app"), Assert.Single(entity.IdentifyingAttributes));
        Assert.Empty(entity.DescriptiveAttributes);
        Assert.Null(entity.SchemaUrl);
    }

    [Fact]
    public void OtelEntitiesEnvVar_IdentifyingAndDescriptiveAttributesAndSchemaUrl()
    {
        var entities = Detect("service{service.name=my-app,service.instance.id=instance-1}[service.version=1.0.0]@https://opentelemetry.io/schemas/1.21.0");

        var entity = Assert.Single(entities);
        Assert.Equal("service", entity.Type);
        Assert.Contains(new KeyValuePair<string, object>("service.name", "my-app"), entity.IdentifyingAttributes);
        Assert.Contains(new KeyValuePair<string, object>("service.instance.id", "instance-1"), entity.IdentifyingAttributes);
        Assert.Contains(new KeyValuePair<string, object>("service.version", "1.0.0"), entity.DescriptiveAttributes);
        Assert.Equal("https://opentelemetry.io/schemas/1.21.0", entity.SchemaUrl);
    }

    [Fact]
    public void OtelEntitiesEnvVar_MultipleEntities()
    {
        var entities = Detect("service{service.name=my-app}[service.version=1.0.0]@https://opentelemetry.io/schemas/1.21.0;host{host.id=host-123}");

        Assert.Collection(
            entities,
            entity => Assert.Equal("service", entity.Type),
            entity =>
            {
                Assert.Equal("host", entity.Type);
                Assert.Equal(new KeyValuePair<string, object>("host.id", "host-123"), Assert.Single(entity.IdentifyingAttributes));
            });
    }

    [Fact]
    public void OtelEntitiesEnvVar_EmptySegmentsAreIgnored()
    {
        var entities = Detect(";;service{service.name=my-app};;host{host.id=host-123};;");

        Assert.Equal(2, entities.Count);
    }

    [Fact]
    public void OtelEntitiesEnvVar_MissingIdentifyingAttributesIsIgnored()
    {
        var entities = Detect("service{}");

        Assert.Empty(entities);
    }

    [Fact]
    public void OtelEntitiesEnvVar_UnparseableEntityIsIgnored()
    {
        var entities = Detect("not a valid entity");

        Assert.Empty(entities);
    }

    private static IReadOnlyList<Entity> Detect(string? envVarValue = null)
    {
        if (envVarValue != null)
        {
            Environment.SetEnvironmentVariable(OtelEntitiesEnvVarDetector.EnvVarKey, envVarValue);
        }

        return [.. new OtelEntitiesEnvVarDetector(new ConfigurationBuilder().AddEnvironmentVariables().Build()).Detect()];
    }
}
