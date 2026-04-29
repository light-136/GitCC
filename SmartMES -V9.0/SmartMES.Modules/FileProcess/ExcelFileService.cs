using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace SmartMES.Modules.FileProcess
{
    /// <summary>
    /// Excel文件处理服务（EPPlus库）
    /// EPPlus命名空间是 OfficeOpenXml，不是 EPPlus
    /// </summary>
    public class ExcelFileService : IFileService
    {
        public string SupportedExtensions => ".xlsx";

        /// <summary>
        /// 自动补齐：ExcelFileService 方法说明。
        /// </summary>
        public ExcelFileService()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        /// <summary>读取Excel第一个Sheet的所有行列</summary>
        public async Task<FileResult> ReadAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var pkg = new ExcelPackage(new FileInfo(filePath));
                    var ws = pkg.Workbook.Worksheets.FirstOrDefault();
                    if (ws == null)
                        return new FileResult { Success = false, Message = "工作簿为空" };

                    int maxRow = ws.Dimension?.Rows ?? 0;
                    int maxCol = ws.Dimension?.Columns ?? 0;
                    var rows = new List<string[]>();

                    for (int r = 1; r <= maxRow; r++)
                    {
                        var cells = new string[maxCol];
                        for (int c = 1; c <= maxCol; c++)
                            cells[c - 1] = ws.Cells[r, c].Text ?? string.Empty;
                        rows.Add(cells);
                    }

                    return new FileResult
                    {
                        Success = true,
                        Message = $"Sheet: {ws.Name}, {maxRow}行 × {maxCol}列",
                        Rows = rows
                    };
                }
                catch (Exception ex)
                {
                    return new FileResult { Success = false, Message = ex.Message };
                }
            });
        }

        /// <summary>写入Excel：首行表头蓝色加粗，奇偶行交替背景</summary>
        public async Task<bool> WriteAsync(string filePath, List<string[]> rows, string[]? headers = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var pkg = new ExcelPackage();
                    var ws = pkg.Workbook.Worksheets.Add("Data");
                    int startRow = 1;

                    if (headers != null)
                    {
                        for (int c = 0; c < headers.Length; c++)
                        {
                            var cell = ws.Cells[1, c + 1];
                            cell.Value = headers[c];
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0, 112, 192));
                            cell.Style.Font.Color.SetColor(Color.White);
                        }
                        startRow = 2;
                    }

                    for (int r = 0; r < rows.Count; r++)
                    {
                        var row = rows[r];
                        for (int c = 0; c < row.Length; c++)
                            ws.Cells[startRow + r, c + 1].Value = row[c];

                        // 交替行背景
                        if (r % 2 == 1)
                        {
                            var range = ws.Cells[startRow + r, 1, startRow + r, row.Length];
                            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(235, 241, 255));
                        }
                    }

                    ws.Cells.AutoFitColumns();
                    pkg.SaveAs(new FileInfo(filePath));
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
