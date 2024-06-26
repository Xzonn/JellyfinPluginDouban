﻿using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Douban;

public class Constants
{
    public const string ProviderName = "Douban";

    public const string ProviderId = "DoubanID";

    public const string ProviderId_Old = "Douban ID";
    public const string ProviderId_OpenDouban = "OpenDoubanID";

    public const string PersonageId = "DoubanPersonageID";
    public const string PersonageId_Old = "Douban Personage ID";

    public const string PluginName = "Douban";

    public const string PluginGuid = "f962a752-15e8-4a43-b42a-6d9cfba35ce1";

    public const string Language = "zh";

#pragma warning disable CA2211 // 非常量字段应当不可见
    public static JsonSerializerOptions JsonSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
#pragma warning restore CA2211 // 非常量字段应当不可见
}
