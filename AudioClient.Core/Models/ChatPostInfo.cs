using System;
using System.Collections.Generic;

namespace AudioClient.Core.Models;

public record ChatContent(string Type, string? Text, string? ImageUrl);

public record ChatPostInfo(
    string SlotId,
    DateTime Time,
    string MachineId,
    string Username,
    string? IconUrl,
    List<ChatContent> Contents);
