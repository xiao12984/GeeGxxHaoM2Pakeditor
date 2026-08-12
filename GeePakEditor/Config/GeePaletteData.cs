namespace GeePakEditor.Config;

/// <summary>
/// GEE 8 位图片使用的固定 BGRA 调色板。
/// </summary>
internal static class GeePaletteData
{
    /// <summary>
    /// 返回解码后的 256 色 BGRA 调色板。
    /// </summary>
    public static byte[] CreatePalette()
    {
        const string base64 =
            "AAAAAAAAgP8AgAD/AICA/4AAAP+AAID/gIAA/8DAwP+XgFX/yLmd/3Nze/8pKS3/UlJa/1paY/85OUL/GBgd/xAQGP8YGCn/CAgQ/3F58v9fZ+H/Wlr//zEx//9SWtb/ABCU/xgplP8ACDn/ABBz/wAYtf9SY73/EBhC/5mq//8AEFr/KTlz/zFKpf9ze5T/MVK9/xAhUv8YMXv/EBgt/zFKjP8AKZT/ADG9/1Jzxv8YMWv/QmvG/wBKzv85Y6X/GDFa/wAQKv8ACBX/ABg6/wAACP8AACn/AABK/wAAnf8AANz/AADe/wAA+/9Sc5z/SmuU/ylKc/8YMVL/GEqM/xFEiP8AIUr/EBgh/1qU1v8ha8b/AGvv/wB3//+ElKX/ITFC/wgQGP8IGCn/ABAh/xgpOf85Y4z/EClC/xhCa/8YSnv/AEqU/3uEjP9aY2v/OUJK/xghKf8pOUb/lKW1/1pre/+Usc7/c4yl/1pzjP9zlLX/c6XW/0ql7/+Mxu//QmN7/zlWa/9alL3/ADlj/63G1v8pQlL/GGOU/63W7/9jjKX/Slpj/3ulvf8YQlr/MYy9/ykxNf9jhJT/Smt7/1qMpf8pSlr/OXuc/xAxQv8hre//ABAY/wAhKf8Aa5z/WoSU/xhCUv8pWmv/IWN7/yF7nP8Apd7/OVJa/xApMf97vc7/OVpj/0qElP8ppcb/GJwQ/0qMQv9CjDH/KZQQ/xAYCP8YGAj/ECkI/ylCGP+ttaX/c3Nr/ykpGP9KQhj/SkIx/97GY///3UT/79aM/zlrc/853vf/jO/3/wDn9/9aa2v/pYxa/++1Of/OnEr/tYQx/2tSMf/W3t7/tb29/4SMjP/e9/f/GAgA/zkYCP8pEAj/ABgI/wApCP+lUgD/3nsA/0opEP9rORD/jFIQ/6VaIf9aMRD/hEIQ/4RSMf8xIRj/e1pK/6VrUv9jOSn/3koQ/yEpKf85Skr/GCkp/ylKSv9Ce3v/Spyc/ylaWv8UQkL/ADk5/wBZWf8sNcr/IXNr/wAxKf8QOTH/GDkx/wBKQv8YY1L/KXNa/xhKMf8AIRj/ADEY/xA5GP9KhGP/Sr1r/0q1Y/9KvWP/Spxa/zmMSv9KxmP/StZj/0qEUv8pczH/WsZj/0q9Uv8A/xD/GCkY/0qISv9K50r/AFoA/wCIAP8AlAD/AN4A/wDuAP8A+wD/lFpK/7VzY//WjHv/1ntr//+Id//Oxsb/nJSU/8aUnP85MTH/hBgp/4QAGP9SQkr/e0JS/3NaY//3tc7/nHuM/8wid///qt3/KrTw/58A3/+zF+P/8Pv//6SgoP+AgID/AAD//wD/AP8A/////wAA//8A/////wD//////w==";
        return Convert.FromBase64String(base64);
    }
}
