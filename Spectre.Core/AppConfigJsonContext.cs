using System.Text.Json.Serialization;

namespace Spectre.Core;

[JsonSerializable(typeof(AppConfig))]
internal partial class AppConfigJsonContext : JsonSerializerContext;
