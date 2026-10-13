using System;

namespace PCL.Neo.Core.Minecraft.ResourceProject.Modrinth;

[Serializable]
public record ModrinthModeratorMessage(
    string message,
    string? body);
