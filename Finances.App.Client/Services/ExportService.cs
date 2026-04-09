using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

public sealed class ExportService
{
    private readonly IJSRuntime _js;

    public ExportService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task ExportExcelAsync(string fileName, string sheetName, string[] headers, object[][] rows)
    {
        await _js.InvokeVoidAsync("financeExport.exportExcel", fileName, sheetName, headers, rows);
    }

    public async Task ExportPdfAsync(string fileName, string title, string[] headers, object[][] rows, string? subtitle = null, bool landscape = false)
    {
        var options = new { subtitle, landscape };
        await _js.InvokeVoidAsync("financeExport.exportPdf", fileName, title, headers, rows, options);
    }
}
