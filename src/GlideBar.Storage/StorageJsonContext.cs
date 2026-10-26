using System.Text.Json.Serialization;
using GlideBar.Core.Models;

namespace GlideBar.Storage;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the types GlideBar persists.
/// Using source generation keeps serialization reflection-free (and therefore trimming-
/// and AOT-friendly) for <see cref="AppSettings"/> and everything it contains
/// (<see cref="PinnedItem"/>, <see cref="BarPlacement"/>, the enums, …).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(PinnedItem))]
[JsonSerializable(typeof(BarPlacement))]
internal sealed partial class StorageJsonContext : JsonSerializerContext
{
}
