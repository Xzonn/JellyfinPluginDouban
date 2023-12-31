﻿using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Douban;

public class Constants
{
    public const string ProviderName = "Douban";

    public const string ProviderId = "Douban ID";

    public const string OddbId = "OpenDoubanID";

    public const string PluginName = "Douban";

    public const string PluginGuid = "f962a752-15e8-4a43-b42a-6d9cfba35ce1";

    public const string Language = "zh";

    public static JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
