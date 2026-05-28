using ClosedXML.Excel;
using System.Data;

namespace HandWritten_OCR.Services;

public class ExcelService
{
    public List<string> ReadHeaders(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheets.First();
        var headers = new List<string>();
        foreach (var cell in ws.Row(1).CellsUsed())
        {
            var text = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(text))
                headers.Add(text);
        }
        return headers;
    }

    public void Export(DataTable table, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("OCR Data");

        for (int c = 0; c < table.Columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = table.Columns[c].ColumnName;
            cell.Style.Font.Bold = true;
        }

        for (int r = 0; r < table.Rows.Count; r++)
            for (int c = 0; c < table.Columns.Count; c++)
                ws.Cell(r + 2, c + 1).Value = table.Rows[r][c]?.ToString() ?? string.Empty;

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }
}
