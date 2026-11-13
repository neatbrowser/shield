# NeatShield

Ứng dụng Windows chặn quảng cáo và theo dõi, kiểu AdGuard, xây trên hai repo:

- [`neatbrowser/neat-adblock`](https://github.com/neatbrowser/neat-adblock) — engine lọc (`neat_adblock.dll`, Rust, bọc adblock-rust).
- [`neatbrowser/neat-filters`](https://github.com/neatbrowser/neat-filters) — bộ luật lọc, phát hành tự động trên GitHub Releases.

> **Trạng thái:** bản đầu tiên (v0.1). Lõi (`NeatShield.Core`) đã được chạy thử với engine thật và bản phát hành neat-filters thật trên Linux. **Ứng dụng WinUI, proxy Titanium, chứng chỉ, cài proxy Windows và khay hệ thống chưa từng được biên dịch hay chạy** (môi trường viết code không có Windows/NuGet). Hãy coi chúng là code chưa kiểm thử. Xem mục “Đã kiểm tra gì”.

## Cách hoạt động

```
Trình duyệt / ứng dụng ──► proxy HTTP(S) cục bộ 127.0.0.1:8877 ──► Internet
                              │  (Titanium.Web.Proxy, chứng chỉ gốc riêng của máy này)
                              ▼
                         neat_adblock.dll  (neat_check_request, neat_cosmetic_resources, ...)
```

1. Bật bảo vệ → NeatShield mở proxy trên 127.0.0.1 và đặt nó làm proxy hệ thống của Windows (người dùng hiện tại). Cài đặt proxy cũ của bạn được lưu lại và **trả lại** khi tắt/thoát, và cả khi khởi động lại sau sự cố.
2. Với HTTPS, NeatShield giải mã bằng chứng chỉ gốc tạo trên chính máy này (khóa riêng được bảo vệ bằng DPAPI, không rời máy). Ngân hàng/đăng nhập Microsoft và các tên miền bạn loại trừ được cho đi thẳng, không giải mã.
3. Mỗi yêu cầu được engine kiểm tra: cho qua, chặn (trả phản hồi trống phù hợp loại: ảnh 1×1, script/CSS rỗng, 204…), hoặc thay bằng tài nguyên vô hại (luật `$redirect`). Tham số theo dõi (`$removeparam`) được bỏ khỏi URL.
4. Trang HTML được chèn CSS ẩn phần tử và script (scriptlet) của engine, kèm nonce vào Content-Security-Policy để trang không chặn chúng.
5. Bộ lọc tự cập nhật từ `neat-filters` (mặc định mỗi 6 giờ): chỉ tải tệp đổi (theo SHA-256), kiểm tra kích thước + hash, xây xong mới thay thế; engine được nạp lại không làm gián đoạn duyệt web.

## Giao diện

Trang chủ (công tắc lớn “Bảo vệ đang bật”, số liệu), Bộ lọc (phiên bản, cập nhật ngay), Thống kê (tên miền bị chặn nhiều nhất, nhật ký gần đây), Cài đặt (cổng, HTTPS, loại trừ, tự khởi động, chứng chỉ, “trả lại proxy Windows”) và biểu tượng khay hệ thống (mở, bật/tắt, cập nhật, thoát). Đóng cửa sổ chỉ thu nhỏ vào khay.

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `src/NeatShield.Core` | Toàn bộ logic: P/Invoke engine, tải/kiểm tra bộ lọc, proxy, chứng chỉ, proxy hệ thống, thống kê, `ShieldRuntime` (điểm ghép chung). Không phụ thuộc giao diện. |
| `src/NeatShield.App` | WinUI 3 (không đóng gói MSIX), các trang, khay hệ thống. |
| `src/NeatShield.Cli` | `neatshield run | status | update | check <url> | trust-ca | untrust-ca | reset-proxy`. |
| `tests/NeatShield.Core.Tests` | Bộ kiểm thử dạng console (không cần framework): `dotnet run --project tests/NeatShield.Core.Tests`. |
| `tools/` | `fetch-native.ps1`, `fetch-filters.ps1`, `build.ps1`. |

## Build (trên Windows)

Cần .NET SDK 10 (và Rust + MSVC nếu muốn tự build `neat_adblock.dll` bằng `-Build`).

```powershell
./tools/build.ps1            # tải neat_adblock.dll + bộ lọc, chạy test, publish ra dist\x64
./tools/build.ps1 -Arch arm64
```

Chạy `dist\x64\NeatShield.exe`. Lần đầu bật bảo vệ, Windows sẽ hỏi xác nhận cài chứng chỉ gốc của NeatShield cho người dùng hiện tại.

Nếu mất mạng sau khi app sập: chạy `neatshield reset-proxy` (hoặc nút “Trả lại cài đặt proxy của Windows”).

## Giới hạn đã biết

- **Firefox** dùng kho chứng chỉ riêng: nhập `NeatShield-Root-CA.cer` thủ công (hướng dẫn trong Cài đặt) hoặc bật `security.enterprise_roots.enabled`.
- Ứng dụng ghim chứng chỉ (certificate pinning) sẽ lỗi khi bị giải mã: thêm tên miền vào danh sách loại trừ. Một số tên miền Microsoft/Dropbox/Webex đã được loại sẵn.
- Chưa có: chặn popup, luật ẩn dạng thủ tục (`:has-text`…) và ẩn phần tử chung theo class/id (cần script phía trang), CSP đặt qua thẻ `<meta>`, chạy như dịch vụ Windows, bộ lọc tự chọn của người dùng, tiếng Anh/đa ngôn ngữ (giao diện đang là tiếng Việt).
- Chứng chỉ gốc có hiệu lực ~13 tháng; khi hết hạn app tạo cái mới và cần cài lại.
- Trình cập nhật không đi qua proxy hệ thống (để khỏi vòng lặp), nên mạng bắt buộc dùng proxy công ty sẽ không cập nhật được.

## Đã kiểm tra gì

| Phần | Cách kiểm tra |
|---|---|
| Đọc/kiểm tra manifest, tải bản phát hành thật, kiểm tra SHA-256, cache engine | Chạy thật với `neat-filters` release thật (lần 2 = “up to date”, nạp lại từ cache) |
| Engine qua P/Invoke | Chạy thật với `libneat_adblock.so` build từ repo (chặn/redirect/CSP/cosmetic) |
| Phân loại request, luật host, CSP nonce, chèn HTML (cả mức byte), data: URL, cài đặt, DPAPI-wrapper (nhánh không-Windows) | `tests/NeatShield.Core.Tests` — chạy qua |
| `ProxyService`, `CertificateService`, `ShieldRuntime`, CLI | **Chỉ biên dịch** với stub mô phỏng chữ ký Titanium; chưa chạy với Titanium thật |
| `SystemProxy` (registry), `StartupRegistration`, DPAPI, WinUI App, khay | **Chưa biên dịch, chưa chạy** (cần Windows) |
