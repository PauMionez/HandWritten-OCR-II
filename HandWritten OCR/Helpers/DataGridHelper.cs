using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HandWritten_OCR.Helpers
{
     class DataGridHelper
    {
        /// <summary>
        /// Writes OCR text into the currently selected cell and advances to the
        /// next editable column (skipping ImageName which is auto-managed).
        /// </summary>
        /// <returns>
        /// The name of the column actually written (after the ImageName/first-editable
        /// resolution), or <c>null</c> if nothing was written. Callers that don't need
        /// it can ignore the return value.
        /// </returns>
        public string? FillSelectedCell(string text, DataTable gridData, int selectedRowIndex, string selectedCellColumn, Action<int, int> RequestCellFocus)
        {
            if (gridData is null || selectedRowIndex < 0) return null;
            if (selectedRowIndex >= gridData.Rows.Count) return null;

            // Skip ImageName column — never overwrite it via OCR
            if (selectedCellColumn == "ImageName" || selectedCellColumn is null)
                selectedCellColumn = FirstEditableColumn(gridData);

            if (selectedCellColumn is null || !gridData.Columns.Contains(selectedCellColumn)) return null;

            // Snap a misread month to the nearest known form — date columns only.
            text = MonthFieldCorrector.Apply(text, selectedCellColumn);

            gridData.Rows[selectedRowIndex][selectedCellColumn] = text;
            string writtenColumn = selectedCellColumn;

            string? next = NextEditableColumn(selectedCellColumn, gridData);
            if (next is not null) selectedCellColumn = next;
            RequestCellFocus?.Invoke(selectedRowIndex, GetColumnIndex(selectedCellColumn, gridData));

            return writtenColumn;
        }

        /// <summary>Returns the first column that is not "ImageName".</summary>
        public string FirstEditableColumn(DataTable gridData)
        {
            if (gridData is null) return null;
            foreach (DataColumn col in gridData.Columns)
            {
                if (col.ColumnName != "ImageName") return col.ColumnName;
            }

            return string.Empty;
        }

        /// <summary>Returns the next non-ImageName column after <paramref name="current"/>, or null if none.</summary>
        public string? NextEditableColumn(string current, DataTable gridData)
        {
            if (gridData is null) return null;
            int idx = GetColumnIndex(current, gridData);
            for (int i = idx + 1; i < gridData.Columns.Count; i++)
                if (gridData.Columns[i].ColumnName != "ImageName")
                    return gridData.Columns[i].ColumnName;
            return null;
        }

        public int GetColumnIndex(string colName, DataTable gridData)
        {
            if (gridData is null || !gridData.Columns.Contains(colName)) return 0;
            return gridData.Columns[colName]!.Ordinal;
        }
    }
}
