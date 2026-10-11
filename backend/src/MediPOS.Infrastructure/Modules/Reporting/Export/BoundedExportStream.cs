using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;

namespace MediPOS.Infrastructure.Modules.Reporting.Export;

internal sealed class BoundedExportStream(CancellationToken token) : MemoryStream
{
    private void Check(long end)
    {
        token.ThrowIfCancellationRequested();
        if (end > ReportExportLimits.MaximumBytes) throw new ApplicationErrorException(ReportExportErrors.TooLarge);
    }
    public override void Write(byte[] buffer, int offset, int count) { Check(Position + count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(Position + buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Check(Position + 1); base.WriteByte(value); }
    public override void SetLength(long value) { Check(value); base.SetLength(value); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); Write(buffer, offset, count); return Task.CompletedTask; }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Write(buffer.Span); return ValueTask.CompletedTask; }
}
