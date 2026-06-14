using ClosedXML.Excel;
using SecurityRoles.Models;

namespace SecurityRoles.Services;

/// <summary>Writes role data to .xlsx workbooks for download.</summary>
public static class RoleExcelExporter
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#F2F2F7");

    public static void ExportMembers(string path, RoleItem role, IReadOnlyList<RoleMemberItem> members)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Members");

        WriteTitle(ws, role, $"Members ({members.Count})", lastColumn: 3);

        const int headerRow = 3;
        ws.Cell(headerRow, 1).Value = "Kind";
        ws.Cell(headerRow, 2).Value = "Name";
        ws.Cell(headerRow, 3).Value = "Email / Detail";
        StyleHeader(ws.Range(headerRow, 1, headerRow, 3));

        var r = headerRow + 1;
        foreach (var m in members)
        {
            ws.Cell(r, 1).Value = m.Kind;
            ws.Cell(r, 2).Value = m.Title;
            ws.Cell(r, 3).Value = m.Secondary;
            r++;
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    public static void ExportPrivileges(string path, RoleItem role, IReadOnlyList<RolePrivilegeRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Table permissions");

        string[] headers =
            ["Table", "Logical Name", "Owner", "Create", "Read", "Write", "Delete", "Append", "Append To", "Assign", "Share"];

        WriteTitle(ws, role, $"Table permissions ({rows.Count} tables)", lastColumn: headers.Length);

        const int headerRow = 3;
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(headerRow, c + 1).Value = headers[c];
        StyleHeader(ws.Range(headerRow, 1, headerRow, headers.Length));

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Title;
            ws.Cell(r, 2).Value = row.LogicalName;
            ws.Cell(r, 3).Value = row.Owner;
            WriteAccess(ws, r, 4, row.Create);
            WriteAccess(ws, r, 5, row.Read);
            WriteAccess(ws, r, 6, row.Write);
            WriteAccess(ws, r, 7, row.Delete);
            WriteAccess(ws, r, 8, row.Append);
            WriteAccess(ws, r, 9, row.AppendTo);
            WriteAccess(ws, r, 10, row.Assign);
            WriteAccess(ws, r, 11, row.Share);
            r++;
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Column(1).Width = 30;
        ws.Column(2).Width = 28;
        for (var c = 3; c <= headers.Length; c++)
            ws.Column(c).Width = 13;
        wb.SaveAs(path);
    }

    private static void WriteTitle(IXLWorksheet ws, RoleItem role, string subtitle, int lastColumn)
    {
        ws.Cell(1, 1).Value = role.Title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, lastColumn).Merge();

        var bu = string.IsNullOrWhiteSpace(role.BusinessUnit) ? "" : $"Business Unit: {role.BusinessUnit}   ·   ";
        ws.Cell(2, 1).Value = $"{bu}{subtitle}";
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#636366");
        ws.Range(2, 1, 2, lastColumn).Merge();
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = HeaderFill;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor = XLColor.FromHtml("#C7C7CC");
    }

    private static void WriteAccess(IXLWorksheet ws, int row, int col, AccessCell cell)
    {
        if (!cell.Applicable) return; // table doesn't support this privilege → leave blank

        var c = ws.Cell(row, col);
        c.Value = cell.Full;
        c.Style.Fill.BackgroundColor = XLColor.FromHtml(cell.Color);
        c.Style.Font.FontColor = XLColor.FromHtml(cell.TextColor);
        c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }
}
