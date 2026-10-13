using System;

namespace PCL.Neo.Core.Minecraft.ResourceProject.Modrinth;

[Serializable]
public record ModrinthGallery(
    string url,
    bool featured,
    string? title,
    string? description,
    string created,
    int ordering);
