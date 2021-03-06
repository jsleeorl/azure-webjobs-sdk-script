﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Moq;
using Newtonsoft.Json;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretManagerTests
    {
        private ScriptSettingsManager _settingsManager = ScriptSettingsManager.Instance;

        [Fact]
        public void MergedSecrets_PrioritizesFunctionSecrets()
        {
            using (var directory = new TempDirectory())
            {
                string hostSecrets =
                    @"{
    'masterKey': {
        'name': 'master',
        'value': '1234',
        'encrypted': false
    },
    'functionKeys': [
        {
            'name': 'Key1',
            'value': 'HostValue1',
            'encrypted': false
        },
        {
            'name': 'Key3',
            'value': 'HostValue3',
            'encrypted': false
        }
    ]
}";
                string functionSecrets =
                    @"{
    'keys': [
        {
            'name': 'Key1',
            'value': 'FunctionValue1',
            'encrypted': false
        },
        {
            'name': 'Key2',
            'value': 'FunctionValue2',
            'encrypted': false
        }
    ]
}";
                File.WriteAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName), hostSecrets);
                File.WriteAllText(Path.Combine(directory.Path, "testfunction.json"), functionSecrets);

                IDictionary<string, string> result;
                using (var secretManager = new SecretManager(_settingsManager, directory.Path, NullTraceWriter.Instance))
                {
                    result = secretManager.GetFunctionSecrets("testfunction", true);
                }

                Assert.Contains("Key1", result.Keys);
                Assert.Contains("Key2", result.Keys);
                Assert.Contains("Key3", result.Keys);
                Assert.Equal("FunctionValue1", result["Key1"]);
                Assert.Equal("FunctionValue2", result["Key2"]);
                Assert.Equal("HostValue3", result["Key3"]);
            }
        }

        [Fact]
        public void GetFunctionSecrets_UpdatesStaleSecrets()
        {
            using (var directory = new TempDirectory())
            {
                string functionName = "testfunction";
                string expectedTraceMessage = string.Format(Resources.TraceStaleFunctionSecretRefresh, functionName);
                string functionSecretsJson =
                 @"{
    'keys': [
        {
            'name': 'Key1',
            'value': 'FunctionValue1',
            'encrypted': false
        },
        {
            'name': 'Key2',
            'value': 'FunctionValue2',
            'encrypted': false
        }
    ]
}";
                File.WriteAllText(Path.Combine(directory.Path, functionName + ".json"), functionSecretsJson);

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock();

                IDictionary<string, string> functionSecrets;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    functionSecrets = secretManager.GetFunctionSecrets(functionName);
                }
                // Read the persisted content
                var result = JsonConvert.DeserializeObject<FunctionSecrets>(File.ReadAllText(Path.Combine(directory.Path, functionName + ".json")));
                bool functionSecretsConverted = functionSecrets.Values.Zip(result.Keys, (r1, r2) => string.Equals("!" + r1, r2.Value)).All(r => r);

                Assert.Equal(2, result.Keys.Count);
                Assert.True(functionSecretsConverted, "Function secrets were not persisted");
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Verbose && t.Message.IndexOf(expectedTraceMessage) > -1));
            }
        }

        [Fact]
        public void GetHostSecrets_UpdatesStaleSecrets()
        {
            using (var directory = new TempDirectory())
            {
                string expectedTraceMessage = Resources.TraceStaleHostSecretRefresh;
                string hostSecretsJson =
                    @"{
    'masterKey': {
        'name': 'master',
        'value': '1234',
        'encrypted': false
    },
    'functionKeys': [
        {
            'name': 'Key1',
            'value': 'HostValue1',
            'encrypted': false
        },
        {
            'name': 'Key3',
            'value': 'HostValue3',
            'encrypted': false
        }
    ]
}";
                File.WriteAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName), hostSecretsJson);

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock();

                HostSecretsInfo hostSecrets;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    hostSecrets = secretManager.GetHostSecrets();
                }

                // Read the persisted content
                var result = JsonConvert.DeserializeObject<HostSecrets>(File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName)));
                bool functionSecretsConverted = hostSecrets.FunctionKeys.Values.Zip(result.FunctionKeys, (r1, r2) => string.Equals("!" + r1, r2.Value)).All(r => r);

                Assert.Equal(2, result.FunctionKeys.Count);
                Assert.Equal("!" + hostSecrets.MasterKey, result.MasterKey.Value);
                Assert.True(functionSecretsConverted, "Function secrets were not persisted");
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Verbose && t.Message.IndexOf(expectedTraceMessage) > -1));
            }
        }

        [Fact]
        public void GetHostSecrets_WhenNoHostSecretFileExists_GeneratesSecretsAndPersistsFiles()
        {
            using (var directory = new TempDirectory())
            {
                string expectedTraceMessage = Resources.TraceHostSecretGeneration;
                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false, false);

                HostSecretsInfo hostSecrets;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    hostSecrets = secretManager.GetHostSecrets();
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName));
                HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                Assert.NotNull(hostSecrets);
                Assert.NotNull(persistedSecrets);
                Assert.Equal(1, hostSecrets.FunctionKeys.Count);
                Assert.NotNull(hostSecrets.MasterKey);
                Assert.Equal(persistedSecrets.MasterKey.Value, hostSecrets.MasterKey);
                Assert.Equal(persistedSecrets.FunctionKeys.First().Value, hostSecrets.FunctionKeys.First().Value);
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Verbose && t.Message.IndexOf(expectedTraceMessage) > -1));
            }
        }

        [Fact]
        public void GetFunctionSecrets_WhenNoSecretFileExists_CreatesDefaultSecretAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string functionName = "TestFunction";
                string expectedTraceMessage = string.Format(Resources.TraceFunctionSecretGeneration, functionName);

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false, false);

                IDictionary<string, string> functionSecrets;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    functionSecrets = secretManager.GetFunctionSecrets(functionName);
                }

                bool functionSecretsExists = File.Exists(Path.Combine(directory.Path, "testfunction.json"));

                Assert.NotNull(functionSecrets);
                Assert.True(functionSecretsExists);
                Assert.Equal(1, functionSecrets.Count);
                Assert.Equal(ScriptConstants.DefaultFunctionKeyName, functionSecrets.Keys.First());
                Assert.True(traceWriter.Traces.Any(
                    t => t.Level == TraceLevel.Verbose && t.Message.IndexOf(expectedTraceMessage, StringComparison.OrdinalIgnoreCase) > -1),
                    "Expected Trace message not found");
            }
        }

        [Fact]
        public void AddOrUpdateFunctionSecrets_WithFunctionNameAndNoSecret_GeneratesFunctionSecretsAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string secretName = "TestSecret";
                string functionName = "TestFunction";
                string expectedTraceMessage = string.Format(Resources.TraceAddOrUpdateFunctionSecret, "Function", secretName, functionName, "Created");

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    result = secretManager.AddOrUpdateFunctionSecret(secretName, null, functionName);
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, "testfunction.json"));
                FunctionSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<FunctionSecrets>(secretsJson);

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.NotNull(result.Secret);
                Assert.NotNull(persistedSecrets);
                Assert.Equal(result.Secret, persistedSecrets.Keys.First().Value);
                Assert.Equal(secretName, persistedSecrets.Keys.First().Name, StringComparer.Ordinal);
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Info && t.Message.IndexOf(expectedTraceMessage) > -1),
                    "Expected Trace message not found");
            }
        }

        [Fact]
        public void AddOrUpdateFunctionSecrets_WithFunctionNameAndProvidedSecret_UsesSecretAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string secretName = "TestSecret";
                string functionName = "TestFunction";
                string expectedTraceMessage = string.Format(Resources.TraceAddOrUpdateFunctionSecret, "Function", secretName, functionName, "Created");

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    result = secretManager.AddOrUpdateFunctionSecret(secretName, "TestSecretValue", functionName);
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, "testfunction.json"));
                FunctionSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<FunctionSecrets>(secretsJson);

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.Equal("TestSecretValue", result.Secret, StringComparer.Ordinal);
                Assert.NotNull(persistedSecrets);
                Assert.Equal(result.Secret, persistedSecrets.Keys.First().Value);
                Assert.Equal(secretName, persistedSecrets.Keys.First().Name, StringComparer.Ordinal);
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Info && t.Message.IndexOf(expectedTraceMessage) > -1),
                    "Expected Trace message not found");
            }
        }

        [Fact]
        public void AddOrUpdateFunctionSecrets_WithNoFunctionNameAndProvidedSecret_UsesSecretAndPersistsHostFile()
        {
            using (var directory = new TempDirectory())
            {
                string secretName = "TestSecret";
                string expectedTraceMessage = string.Format(Resources.TraceAddOrUpdateFunctionSecret, "Host", secretName, "host", "Created");

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    result = secretManager.AddOrUpdateFunctionSecret(secretName, "TestSecretValue");
                }

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName));
                HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);
                Key newSecret = persistedSecrets.FunctionKeys.FirstOrDefault(k => string.Equals(k.Name, secretName, StringComparison.Ordinal));

                Assert.Equal(OperationResult.Created, result.Result);
                Assert.Equal("TestSecretValue", result.Secret, StringComparer.Ordinal);
                Assert.NotNull(persistedSecrets);
                Assert.NotNull(newSecret);
                Assert.Equal(result.Secret, newSecret.Value);
                Assert.Equal(secretName, newSecret.Name, StringComparer.Ordinal);
                Assert.NotNull(persistedSecrets.MasterKey);
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Info && t.Message.IndexOf(expectedTraceMessage) > -1),
                    "Expected Trace message not found");
            }
        }

        [Fact]
        public void SetMasterKey_WithProvidedKey_UsesProvidedKeyAndPersistsFile()
        {
            string testSecret = "abcde0123456789abcde0123456789abcde0123456789";
            using (var directory = new TempDirectory())
            {
                string expectedTraceMessage = "Master key Updated";

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    result = secretManager.SetMasterKey(testSecret);
                }

                bool functionSecretsExists = File.Exists(Path.Combine(directory.Path, "testfunction.json"));

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName));
                HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                Assert.NotNull(persistedSecrets);
                Assert.NotNull(persistedSecrets.MasterKey);
                Assert.Equal(OperationResult.Updated, result.Result);
                Assert.Equal(testSecret, result.Secret);
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Info && t.Message.IndexOf(expectedTraceMessage) > -1));
            }
        }

        [Fact]
        public void SetMasterKey_WithoutProvidedKey_GeneratesKeyAndPersistsFile()
        {
            using (var directory = new TempDirectory())
            {
                string expectedTraceMessage = "Master key Created";

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false);

                KeyOperationResult result;
                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                using (var secretManager = new SecretManager(directory.Path, mockValueConverterFactory.Object, traceWriter))
                {
                    result = secretManager.SetMasterKey();
                }

                bool functionSecretsExists = File.Exists(Path.Combine(directory.Path, "testfunction.json"));

                string secretsJson = File.ReadAllText(Path.Combine(directory.Path, ScriptConstants.HostMetadataFileName));
                HostSecrets persistedSecrets = ScriptSecretSerializer.DeserializeSecrets<HostSecrets>(secretsJson);

                Assert.NotNull(persistedSecrets);
                Assert.NotNull(persistedSecrets.MasterKey);
                Assert.Equal(OperationResult.Created, result.Result);
                Assert.Equal(result.Secret, persistedSecrets.MasterKey.Value);
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Info && t.Message.IndexOf(expectedTraceMessage) > -1),
                    "Expected Trace message not found");
            }
        }

        [Fact]
        public void Constructor_WithCreateHostSecretsIfMissingSet_CreatesHostSecret()
        {
            var secretsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var hostSecretPath = Path.Combine(secretsPath, ScriptConstants.HostMetadataFileName);
            try
            {
                string expectedTraceMessage = Resources.TraceHostSecretGeneration;
                bool preExistingFile = File.Exists(hostSecretPath);

                Mock<IKeyValueConverterFactory> mockValueConverterFactory = GetConverterFactoryMock(false, false);

                var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
                var secretManager = new SecretManager(secretsPath, mockValueConverterFactory.Object, traceWriter, true);
                bool fileCreated = File.Exists(hostSecretPath);

                Assert.False(preExistingFile);
                Assert.True(fileCreated);
                Assert.True(traceWriter.Traces.Any(t => t.Level == TraceLevel.Verbose && t.Message.IndexOf(expectedTraceMessage) > -1));
            }
            finally
            {
                Directory.Delete(secretsPath, true);
            }
        }

        private Mock<IKeyValueConverterFactory> GetConverterFactoryMock(bool simulateWriteConversion = true, bool setStaleValue = true)
        {
            var mockValueReader = new Mock<IKeyValueReader>();
            mockValueReader.Setup(r => r.ReadValue(It.IsAny<Key>()))
                .Returns<Key>(k => new Key(k.Name, k.Value) { IsStale = setStaleValue ? true : k.IsStale });

            var mockValueWriter = new Mock<IKeyValueWriter>();
            mockValueWriter.Setup(r => r.WriteValue(It.IsAny<Key>()))
                .Returns<Key>(k => new Key(k.Name, simulateWriteConversion ? "!" + k.Value : k.Value) { IsEncrypted = simulateWriteConversion });

            var mockValueConverterFactory = new Mock<IKeyValueConverterFactory>();
            mockValueConverterFactory.Setup(f => f.GetValueReader(It.IsAny<Key>()))
                .Returns(mockValueReader.Object);
            mockValueConverterFactory.Setup(f => f.GetValueWriter(It.IsAny<Key>()))
                .Returns(mockValueWriter.Object);

            return mockValueConverterFactory;
        }
    }
}
