using System;

namespace PCL.Neo.Core.Minecraft.ResourceProject.Modrinth;

[Serializable]
public record ModrinthLicense(
    string id,
    string name,
    string? url);
