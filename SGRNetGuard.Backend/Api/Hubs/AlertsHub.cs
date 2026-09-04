using Microsoft.AspNetCore.SignalR;

namespace SGRNetGuard.Api.Hubs;

/// <summary>
/// Hub SignalR: mọi trình duyệt IT đang mở Dashboard sẽ tự động nhận sự kiện này
/// ngay khi có máy user nào bị cảnh báo hiệu năng, không cần refresh trang.
/// </summary>
public class AlertsHub : Hub
{
    // Không cần method nào ở đây - client chỉ lắng nghe (subscribe),
    // server chủ động gọi qua IHubContext<AlertsHub> ở Program.cs khi có cảnh báo mới.
}
