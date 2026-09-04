# MakeSignPng

<img src="docs-icon.png" alt="MakeSignPng icon" width="96" align="right" />

Ứng dụng **Windows Forms (.NET 8)** đơn giản để biến ảnh chụp/scan chữ ký (nền giấy trắng)
thành **ảnh PNG chữ ký trong suốt (không nền)**, tự cắt sát nét và xuất ra hai kích thước:

- **280 × 140**
- **70 × 70**

Ảnh gốc có thể ở nhiều kích thước, nhiều định dạng (PNG, JPG, BMP, TIFF, GIF).

## Tính năng

- **Tách nền tự động**: các điểm ảnh sáng (giấy) thành trong suốt, nét mực giữ lại; viền nét
  được làm mờ (anti-alias) theo alpha nên không bị răng cưa.
- **Tự động đoán ngưỡng** dựa trên độ sáng viền ảnh khi mở tệp (có nút *Tự động ngưỡng*).
- **Hai thanh trượt** tinh chỉnh trực tiếp:
  - *Ngưỡng nền*: sáng hơn mức này → trong suốt (tăng nếu còn sót nền xám).
  - *Ngưỡng mực*: tối hơn mức này → đục hoàn toàn (giảm nếu nét bị mờ/mất).
- **Lọc nhiễu (despeckle)**: xóa các đốm mực nhỏ lẻ.
- **Lề chừa (padding)**: khoảng trắng chừa quanh chữ ký sau khi cắt.
- **Đổi màu mực (tùy chọn)**: ép toàn bộ nét về một màu đồng nhất (vd. xanh/đen).
- **Dán ảnh từ clipboard**: bấm *Dán từ clipboard* hoặc `Ctrl+V` để lấy thẳng ảnh chụp màn
  hình (hoặc ảnh/tệp ảnh đã Copy) mà không cần lưu ra file trước.
- **Ô nhập tên file lưu**: tự gợi ý theo ảnh nguồn nhưng sửa được tùy ý; khi lưu sẽ tự thêm
  hậu tố `_280x140` / `_70x70`.
- **Nhớ cấu hình lần cuối**: các ngưỡng, lọc nhiễu, lề, màu mực... được lưu lại và tự nạp
  cho lần chạy sau (chỉ trừ tên file). Có tùy chọn *Tự động ngưỡng khi mở ảnh* (bật/tắt).
- **Xem trước trực tiếp** trên nền ô caro (thấy rõ vùng trong suốt), kèm ô xem trước đúng
  280×140 và 70×70.
- **Xuất chất lượng cao**: khi lưu, ảnh được xử lý lại từ **ảnh gốc đầy đủ** (không phải bản
  thu nhỏ dùng để xem trước).

## Cách dùng

1. Bấm **Mở ảnh…** rồi chọn ảnh chữ ký gốc.
2. Ứng dụng tự đoán ngưỡng và hiển thị kết quả đã tách nền + cắt.
3. Nếu chưa ưng ý, kéo **Ngưỡng nền / Ngưỡng mực**, chỉnh **Lọc nhiễu** và **Lề chừa**.
   - Còn sót bóng/nền xám → **tăng Ngưỡng nền**.
   - Nét bị đứt/mờ → **giảm Ngưỡng mực**.
   - Còn chấm mực lẻ → **tăng Lọc nhiễu**.
4. Bấm **Lưu 280×140**, **Lưu 70×70**, hoặc **Lưu cả hai…** (tự thêm hậu tố
   `_280x140` / `_70x70`).

## Tải bản chạy sẵn (không cần cài gì)

👉 **[Tải MakeSignPng.exe](https://github.com/virutkod97/makesignpng/releases/download/latest/MakeSignPng.exe)**
(bản build tự động, self-contained cho Windows x64 — tải về là chạy, không cần cài .NET).

Trang release: https://github.com/virutkod97/makesignpng/releases/tag/latest

> Lần đầu chạy, Windows SmartScreen có thể cảnh báo do file chưa ký số:
> bấm **More info → Run anyway**.

Mỗi lần có commit mới lên nhánh, **GitHub Actions** tự build lại và cập nhật file
`MakeSignPng.exe` ở release `latest` (xem tab **Actions** của repo). Có thể bấm
**Run workflow** trong workflow *Build Windows EXE* để build thủ công bất cứ lúc nào.

## Tự build (tuỳ chọn)

- Windows + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (hoặc Visual Studio 2022).

Chạy thử:

```powershell
dotnet run --project src/MakeSignPng/MakeSignPng.csproj -c Release
```

Tạo file .exe chạy độc lập (không cần cài .NET trên máy đích):

```powershell
dotnet publish src/MakeSignPng/MakeSignPng.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true -o publish
```

File `MakeSignPng.exe` nằm trong thư mục `publish`.

> Mở bằng Visual Studio: mở `MakeSignPng.sln` rồi bấm ▶ Run.

## Cấu trúc mã

| Tệp | Vai trò |
|-----|---------|
| `Program.cs` | Điểm vào ứng dụng. |
| `MainForm.cs` | Giao diện, mở/lưu ảnh, điều phối tham số. |
| `SignatureProcessor.cs` | Tách nền, tự cắt, lọc nhiễu, resize theo khung. |
| `CheckerPanel.cs` | Panel nền ô caro để hiển thị vùng trong suốt. |

## Ghi chú kỹ thuật

- Xử lý điểm ảnh bằng `LockBits` (nhanh, không dùng `GetPixel`).
- Alpha nét = ánh xạ tuyến tính giữa *Ngưỡng mực* (đục) và *Ngưỡng nền* (trong suốt), tôn
  trọng alpha sẵn có của ảnh gốc.
- Resize giữ tỉ lệ, canh giữa trong khung đích, phần thừa để trong suốt (nên chữ ký ngang
  đặt trong khung 70×70 sẽ nhỏ theo chiều cao — đúng như mong đợi).
