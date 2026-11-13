using System.Net;

namespace NeatShield.Core.Proxy;

/// <summary>The two pages the proxy shows itself (blocked page, blocked pop-up), in Vietnamese or English.</summary>
public static class PageText
{
    private const string Style =
        "body{font:16px/1.5 system-ui,Segoe UI,sans-serif;background:#f4f6f8;color:#1b2430;margin:0;display:grid;place-items:center;min-height:100vh}"
        + "main{max-width:560px;margin:24px;padding:32px;background:#fff;border-radius:12px;box-shadow:0 2px 12px #0002}"
        + "h1{margin:0 0 8px;font-size:22px;color:#c0392b}code{word-break:break-all;font-size:13px;color:#555}";

    private static string Short(string url) => WebUtility.HtmlEncode(url.Length > 300 ? url[..300] + "…" : url);

    public static string BlockedPage(string url, string language)
    {
        var vi = language == "vi";
        return "<!doctype html><html lang=\"" + (vi ? "vi" : "en") + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
               + "<title>" + (vi ? "Đã bị NeatShield chặn" : "Blocked by NeatShield") + "</title><style>" + Style + "</style></head><body><main>"
               + "<h1>" + (vi ? "Trang này đã bị chặn" : "This page was blocked") + "</h1>"
               + "<p>" + (vi
                   ? "NeatShield đã chặn địa chỉ này vì nó nằm trong một danh sách bộ lọc (mã độc, lừa đảo hoặc theo dõi)."
                   : "NeatShield stopped this address because it is on a filter list (malware, phishing or tracking).") + "</p>"
               + "<p><code>" + Short(url) + "</code></p>"
               + "<p>" + (vi
                   ? "Nếu bạn tin tưởng trang này, hãy thêm tên miền vào danh sách loại trừ trong Cài đặt của NeatShield."
                   : "If you trust this site, add its host to the exclusions in NeatShield settings.") + "</p></main></body></html>";
    }

    public static string PopupPage(string url, string language)
    {
        var vi = language == "vi";
        return "<!doctype html><html lang=\"" + (vi ? "vi" : "en") + "\"><head><meta charset=\"utf-8\"><title>"
               + (vi ? "Đã chặn popup" : "Pop-up blocked") + "</title><style>" + Style + "</style></head><body><main>"
               + "<h1>" + (vi ? "Đã chặn popup bởi NeatShield" : "Pop-up blocked by NeatShield") + "</h1><p><code>" + Short(url) + "</code></p>"
               + "<p>" + (vi
                   ? "Địa chỉ này nằm trong danh sách popup. Nếu bạn mở nó có chủ đích, hãy thêm tên miền vào danh sách loại trừ trong Cài đặt của NeatShield."
                   : "This address is on the pop-up list. If you opened it on purpose, add its host to the exclusions in NeatShield settings.") + "</p>"
               + "<p><a href=\"javascript:history.back()\">" + (vi ? "Quay lại" : "Go back") + "</a></p>"
               + "<script>try{window.close()}catch(e){}</script></main></body></html>";
    }
}
