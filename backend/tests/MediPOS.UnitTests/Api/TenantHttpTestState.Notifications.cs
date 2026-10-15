using System.Reflection;
using MediPOS.Application.Modules.Notifications;
using MediPOS.Domain.Modules.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed partial class TenantHttpTestState
{
    internal List<Notification> Notifications { get; } = [];
    internal List<NotificationReadState> NotificationReads { get; } = [];
    private void ConfigureNotifications(IServiceCollection services)
    {
        Type[] ports = [typeof(INotificationReader), typeof(INotificationReadWriter)];
        foreach (var port in ports)
            services.AddScoped(port, _ => { var proxy = DispatchProxy.Create(port, typeof(Port)); ((Port)proxy).State = this; return proxy; });
    }
    private bool InvokeNotifications(Type type, string method, object?[] args, out object? result)
    {
        result = null;
        if (type == typeof(INotificationReadWriter))
        {
            var candidate = (NotificationReadState)args[0]!; var existing = NotificationReads.SingleOrDefault(read => read.MembershipId == candidate.MembershipId && read.NotificationId == candidate.NotificationId);
            if (existing is null) NotificationReads.Add(candidate);
            result = Task.FromResult(new NotificationReadResult(candidate.NotificationId, (existing ?? candidate).ReadAt)); return true;
        }
        if (type == typeof(INotificationReader))
        {
            if (method == "FindAsync") result = Task.FromResult(Notifications.SingleOrDefault(n => n.TenantId == (Guid)args[0]! && n.Id == (Guid)args[1]!));
            else
            {
                var scope = (NotificationScope)args[0]!;
                var rows = Notifications.Where(n => n.TenantId == scope.TenantId && (!scope.BranchId.HasValue || n.BranchId == scope.BranchId)).Select(n =>
                {
                    var read = NotificationReads.SingleOrDefault(r => r.NotificationId == n.Id && r.MembershipId == scope.MembershipId);
                    return new NotificationDetails { NotificationId = n.Id, Type = n.Type, BranchId = n.BranchId, ProductId = n.BusinessProductId, InventoryLotId = n.InventoryLotId, ExpirationDate = n.ExpirationDate, Title = n.Title, Message = n.Message, CreatedAt = n.CreatedAt, IsRead = read is not null, ReadAt = read?.ReadAt };
                }).ToArray();
                if (method == "CountUnreadAsync") result = Task.FromResult(rows.LongCount(r => !r.IsRead));
                else { var visible = rows.Where(r => !(bool)args[1]! || !r.IsRead).ToArray(); result = Task.FromResult(new NotificationPage(visible.Skip((int)args[2]!).Take((int)args[3]!).ToArray(), visible.LongLength)); }
            }
            AfterPrivateRead?.Invoke(); return true;
        }
        return false;
    }
}
