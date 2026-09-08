// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Resources.Tests;

public class ResourceBuilderTests
{
    [Fact]
    public void ServiceResource_ServiceName()
    {
        var resource = ResourceBuilder.CreateEmpty().AddService("my-service").Build();
        Assert.Equal(2, resource.Attributes.Count());
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceName, "my-service"), resource.Attributes);
        Assert.Single(resource.Attributes, kvp => kvp.Key == ResourceSemanticConventions.AttributeServiceName);
        Assert.True(Guid.TryParse((string)resource.Attributes.Single(kvp => kvp.Key == ResourceSemanticConventions.AttributeServiceInstance).Value, out _));
    }

    [Fact]
    public void ServiceResource_ServiceNameAndInstance()
    {
        var resource = ResourceBuilder.CreateEmpty().AddService("my-service", serviceInstanceId: "123").Build();
        Assert.Equal(2, resource.Attributes.Count());
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceName, "my-service"), resource.Attributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceInstance, "123"), resource.Attributes);
    }

    [Fact]
    public void ServiceResource_ServiceNameAndInstanceAndNamespace()
    {
        var resource = ResourceBuilder.CreateEmpty().AddService("my-service", "my-namespace", serviceInstanceId: "123").Build();
        Assert.Equal(3, resource.Attributes.Count());
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceName, "my-service"), resource.Attributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceInstance, "123"), resource.Attributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceNamespace, "my-namespace"), resource.Attributes);
    }

    [Fact]
    public void ServiceResource_ServiceNameAndInstanceAndNamespaceAndVersion()
    {
        var resource = ResourceBuilder.CreateEmpty().AddService("my-service", "my-namespace", "1.2.3", serviceInstanceId: "123").Build();
        Assert.Equal(4, resource.Attributes.Count());
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceName, "my-service"), resource.Attributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceInstance, "123"), resource.Attributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceNamespace, "my-namespace"), resource.Attributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceVersion, "1.2.3"), resource.Attributes);
    }

    [Fact]
    public void ServiceResource_AutoGenerateServiceInstanceIdOff()
    {
        var resource = ResourceBuilder.CreateEmpty().AddService("my-service", autoGenerateServiceInstanceId: false).Build();
        Assert.Single(resource.Attributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceName, "my-service"), resource.Attributes);
    }

    [Fact]
    public void ServiceResourceGeneratesConsistentInstanceId()
    {
        var firstResource = ResourceBuilder.CreateEmpty().AddService("my-service").Build();

        var firstInstanceIdAttribute = firstResource.Attributes.FirstOrDefault(kvp => kvp.Key == ResourceSemanticConventions.AttributeServiceInstance);

        Assert.NotNull(firstInstanceIdAttribute.Value);

        var secondResource = ResourceBuilder.CreateEmpty().AddService("other-service").Build();

        var secondInstanceIdAttribute = secondResource.Attributes.FirstOrDefault(kvp => kvp.Key == ResourceSemanticConventions.AttributeServiceInstance);

        Assert.NotNull(secondInstanceIdAttribute.Value);

        Assert.Equal(firstInstanceIdAttribute.Value, secondInstanceIdAttribute.Value);
    }

    [Fact]
    public void ClearTest()
    {
        var resource = ResourceBuilder.CreateEmpty()
            .AddTelemetrySdk()
            .Clear()
            .AddService("my-service", autoGenerateServiceInstanceId: false)
            .Build();
        Assert.Single(resource.Attributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceName, "my-service"), resource.Attributes);
    }

    [Fact]
    public void ClearTest_AlsoClearsEntityDetectors()
    {
        var resource = ResourceBuilder.CreateEmpty()
            .AddTelemetrySdk()
            .Clear()
            .AddService("my-service", autoGenerateServiceInstanceId: false)
            .Build();

        var entity = Assert.Single(resource.Entities);
        Assert.Equal(ResourceSemanticConventions.EntityTypeService, entity.Type);
    }

    [Fact]
    public void AddService_RegistersServiceEntity()
    {
        var resource = ResourceBuilder.CreateEmpty()
            .AddService("my-service", "my-namespace", "1.2.3", serviceInstanceId: "123")
            .Build();

        var entity = Assert.Single(resource.Entities);
        Assert.Equal(ResourceSemanticConventions.EntityTypeService, entity.Type);
        Assert.Equal(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceName, "my-service"), Assert.Single(entity.IdentifyingAttributes));
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceNamespace, "my-namespace"), entity.DescriptiveAttributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceVersion, "1.2.3"), entity.DescriptiveAttributes);
        Assert.Contains(new KeyValuePair<string, object>(ResourceSemanticConventions.AttributeServiceInstance, "123"), entity.DescriptiveAttributes);
    }

    [Fact]
    public void AddTelemetrySdk_RegistersTelemetrySdkEntity()
    {
        var resource = ResourceBuilder.CreateEmpty().AddTelemetrySdk().Build();

        var entity = Assert.Single(resource.Entities);
        Assert.Equal(ResourceSemanticConventions.EntityTypeTelemetrySdk, entity.Type);
    }

    [Fact]
    public void AddEntityDetector_AddsAttributesNotAlreadyPresent()
    {
        var entity = new Entity("host", [new("host.id", "host-1")]);

        var resource = ResourceBuilder.CreateEmpty()
            .AddEntityDetector(new StubEntityDetector(entity))
            .Build();

        Assert.Contains(new KeyValuePair<string, object>("host.id", "host-1"), resource.Attributes);
        Assert.Same(entity, Assert.Single(resource.Entities));
    }

    [Fact]
    public void AddEntityDetector_ExplicitResourceAttributesTakePrecedenceOverEntityAttributes()
    {
        var entity = new Entity("host", [new("host.id", "entity-value")]);

        var resource = ResourceBuilder.CreateEmpty()
            .AddAttributes([new KeyValuePair<string, object>("host.id", "explicit-value")])
            .AddEntityDetector(new StubEntityDetector(entity))
            .Build();

        Assert.Contains(new KeyValuePair<string, object>("host.id", "explicit-value"), resource.Attributes);
    }

    private sealed class StubEntityDetector : IEntityDetector
    {
        private readonly Entity entity;

        public StubEntityDetector(Entity entity)
        {
            this.entity = entity;
        }

        public IEnumerable<Entity> Detect() => [this.entity];
    }
}
