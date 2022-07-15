using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using FluentValidation;
using MicroElements.OpenApi.FluentValidation;
using MicroElements.Swashbuckle.FluentValidation;
using MicroElements.Swashbuckle.FluentValidation.Generation;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using SampleWebApi.ValidatorFactories;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MicroElements.Swashbuckle.FluentValidation.Tests
{
    public class UnitTestBase
    {
        public SwaggerGenerator SwaggerGenerator(
            Action<SwaggerGeneratorOptions> configureSwaggerGenerator = null,
            Action<SchemaGeneratorOptions>? configureGenerator = null,
            Action<JsonSerializerOptions>? configureSerializer = null)
        {
            var swaggerGeneratorOptions = new SwaggerGeneratorOptions();
            configureSwaggerGenerator?.Invoke(swaggerGeneratorOptions);

            var schemaGenerator = SchemaGenerator(configureGenerator, configureSerializer);
            var apiDescriptionGroups = new []{new ApiDescriptionGroup("GroupName", new ApiDescription[]
            {
                new ApiDescription()
                {
                    
                }
            })};
            
            var apiDescriptionsProvider = new ApiDescriptionGroupCollectionProvider(
                new ApiDescriptionGroupCollection(apiDescriptionGroups, 1));
            
            return new SwaggerGenerator(swaggerGeneratorOptions, apiDescriptionsProvider, schemaGenerator);
        }

        public SchemaGenerator SchemaGenerator(params IValidator[] validators)
        {
            return SchemaGenerator(options => ConfigureGenerator(options, validators));
        }

        public SchemaGenerator SchemaGenerator(
            Action<SchemaGeneratorOptions>? configureGenerator = null,
            Action<JsonSerializerOptions>? configureSerializer = null)
        {
            var generatorOptions = new SchemaGeneratorOptions();
            configureGenerator?.Invoke(generatorOptions);

            var serializerOptions = new JsonSerializerOptions();
            configureSerializer?.Invoke(serializerOptions);

            return new SchemaGenerator(generatorOptions, new JsonSerializerDataContractResolver(serializerOptions));
        }

        private void ConfigureGenerator(SchemaGeneratorOptions options, IValidator[] validators)
        {
            IValidatorRegistry validatorRegistry = new ValidatorRegistry(validators);
            options.SchemaFilters.Add(new FluentValidationRules(
                validatorRegistry: validatorRegistry,
                nameResolver: new SystemTextJsonNameResolver()));
        }
    }

    public class ApiDescriptionGroupCollectionProvider : IApiDescriptionGroupCollectionProvider
    {
        public ApiDescriptionGroupCollection ApiDescriptionGroups { get; }

        public ApiDescriptionGroupCollectionProvider(ApiDescriptionGroupCollection apiDescriptionGroups)
        {
            ApiDescriptionGroups = apiDescriptionGroups;
        }
    }

    public class SchemaBuilder<T>
    {
        public InlineValidator<T> Validator { get; } = new InlineValidator<T>();

        public SchemaRepository SchemaRepository { get; } = new SchemaRepository();

        private readonly SchemaGenerationOptions _schemaGenerationOptions = new SchemaGenerationOptions();
        private SchemaGenerationSettings _schemaGenerationSettings;

        public SchemaBuilder()
        {
            _schemaGenerationSettings = new SchemaGenerationSettings()
            {
                NameResolver = new SystemTextJsonNameResolver(),
                SchemaIdSelector = new SchemaGeneratorOptions().SchemaIdSelector
            };
        }

        public SchemaBuilder<T> ConfigureSchemaGenerationOptions(
            Action<SchemaGenerationOptions> configureSchemaGenerationOptions,
            Func<SchemaGenerationSettings, SchemaGenerationSettings>? configureSchemaGenerationSettings = null)
        {
            configureSchemaGenerationOptions(_schemaGenerationOptions);

            if(configureSchemaGenerationSettings != null)
                _schemaGenerationSettings = configureSchemaGenerationSettings(_schemaGenerationSettings);
            return this;
        }

        public OpenApiSchema AddRule<TProperty>(
            Expression<Func<T, TProperty>> propertyExpression,
            Action<IRuleBuilderInitial<T, TProperty>>? configureRule = null,
            Action<OpenApiSchema>? schemaCheck = null)
        {
            IRuleBuilderInitial<T, TProperty> ruleBuilder = Validator.RuleFor(propertyExpression);
            configureRule?.Invoke(ruleBuilder);

            var expressionBody = propertyExpression.Body as MemberExpression;
            var schema = SchemaRepository.GenerateSchemaForValidator(Validator, _schemaGenerationOptions, _schemaGenerationSettings);

            PropertyInfo propertyInfo = expressionBody.Member as PropertyInfo;
            string propertyName = _schemaGenerationSettings.NameResolver.GetPropertyName(propertyInfo);

            var property = schema.Properties[propertyName];

            schemaCheck?.Invoke(property);

            return property;
        }
    }

    public static class TestExtensions
    {
        public static OpenApiSchema GenerateSchemaForValidator<T>(
            this SchemaRepository schemaRepository,
            IValidator<T> validator,
            SchemaGenerationOptions? schemaGenerationOptions = null,
            SchemaGenerationSettings? schemaGenerationSettings = null,
            Action<JsonSerializerOptions>? configureSerializer = null)
        {
            SchemaGenerator schemaGenerator = CreateSchemaGenerator(
                new []{ validator },
                schemaGenerationOptions: schemaGenerationOptions,
                schemaGenerationSettings: schemaGenerationSettings,
                configureSerializer: configureSerializer);

            OpenApiSchema schema = schemaGenerator
                .GenerateSchema(typeof(T), schemaRepository);

            if (schema.Reference?.Id != null)
                schema = schemaRepository.Schemas[schema.Reference.Id];

            return schema;
        }

        public static SchemaGenerator CreateSchemaGenerator(
            IValidator[] validators,
            SchemaGenerationOptions? schemaGenerationOptions = null,
            SchemaGenerationSettings? schemaGenerationSettings = null,
            Action<JsonSerializerOptions>? configureSerializer = null)
        {
            return CreateSchemaGenerator(
                configureGenerator: options =>
                {
                    IValidatorRegistry validatorRegistry = new ValidatorRegistry(validators);

                    options.SchemaFilters.Add(new FluentValidationRules(
                        validatorRegistry: validatorRegistry, 
                        rules: null, 
                        loggerFactory: null,
                        schemaGenerationOptions: schemaGenerationOptions != null ? new OptionsWrapper<SchemaGenerationOptions>(schemaGenerationOptions) : null,
                        nameResolver: schemaGenerationSettings?.NameResolver));
                },
                configureSerializer: configureSerializer);
        }

        public static SchemaGenerator CreateSchemaGenerator(
            Action<SchemaGeneratorOptions>? configureGenerator = null,
            Action<JsonSerializerOptions>? configureSerializer = null)
        {
            var generatorOptions = new SchemaGeneratorOptions();
            configureGenerator?.Invoke(generatorOptions);

            var serializerOptions = new JsonSerializerOptions();
            configureSerializer?.Invoke(serializerOptions);

            return new SchemaGenerator(generatorOptions, new JsonSerializerDataContractResolver(serializerOptions));
        }
    }
}