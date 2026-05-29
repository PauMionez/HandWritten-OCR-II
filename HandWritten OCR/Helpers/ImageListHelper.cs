using HandWritten_OCR.Abstract;
using HandWritten_OCR.Models;
using HandWritten_OCR.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;

namespace HandWritten_OCR.Helpers
{
    class ImageListHelper  : ViewBaseModel
    {

        public void AddToImageList(string fullPath, ObservableCollection<ImageItem> imageList)
        {
            if (!imageList.Any(i => i.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
                imageList.Add(new ImageItem { FileName = Path.GetFileName(fullPath), FullPath = fullPath });
        }

        public async Task HandleImageSelectionAsync(ImageItem item, DataTable gridData, int selectedRowIndex, string selectedCellColumn, ObservableCollection<ImageItem> imageList, Action<int, int> RequestCellFocus)
        {   
            if (gridData is null) return; // No template yet — skip row management
            EnsureImageNameColumn(gridData);
            ManageDataGridRowForImage(item.FileName, gridData, selectedRowIndex, selectedCellColumn, imageList, RequestCellFocus);
        }

        /// <summary>
        /// Ensures "ImageName" column exists in the DataTable as the first column.
        /// </summary>
        public void EnsureImageNameColumn(DataTable gridData)
        {
            if (gridData is null || gridData.Columns.Contains("ImageName")) return;
            gridData.Columns.Add("ImageName");
            gridData.Columns["ImageName"]!.SetOrdinal(0);
            OnPropertyChanged(nameof(GridView));
        }

        /// <summary>
        /// Finds or creates a DataGrid row for the given image name, respecting
        /// insertion order so that each image's rows stay grouped together in
        /// the same order they appear in the ImageList.
        /// </summary>
        public void ManageDataGridRowForImage(string imageName, DataTable gridData, int selectedRowIndex, string selectedCellColumn, ObservableCollection<ImageItem> ImageList, Action<int, int> RequestCellFocus)
        {
            if (gridData is null) return;

            DataGridHelper helper = new DataGridHelper();

            // Find the first existing row for this image
            int existingRow = -1;
            if (gridData.Columns.Contains("ImageName"))
            {
                for (int i = 0; i < gridData.Rows.Count; i++)
                {
                    if (gridData.Rows[i]["ImageName"]?.ToString() == imageName)
                    {
                        existingRow = i;
                        break;
                    }
                }
            }

            if (existingRow >= 0)
            {
                selectedRowIndex = existingRow;
            }
            else
            {
                // No row yet — insert at the position that keeps image groups in order
                int insertAt = FirstInsertionIndexFor(imageName, gridData, ImageList);
                var newRow = gridData.NewRow();
                if (gridData.Columns.Contains("ImageName"))
                    newRow["ImageName"] = imageName;
                gridData.Rows.InsertAt(newRow, insertAt);
                selectedRowIndex = insertAt;
            }

            selectedCellColumn = helper.FirstEditableColumn(gridData);
            if (selectedCellColumn is not null)
                RequestCellFocus?.Invoke(selectedRowIndex, helper.GetColumnIndex(selectedCellColumn, gridData));
        }

        /// <summary>
        /// Returns the index where the first row for <paramref name="imageName"/>
        /// should be inserted so that image groups follow ImageList order.
        /// E.g. if Image1 rows already exist, a new Image2 row goes after them.
        /// If later you add another Image1 row (back-navigation), it still goes
        /// before any Image2 rows.
        /// </summary>
        public int FirstInsertionIndexFor(string imageName, DataTable gridData, ObservableCollection<ImageItem> ImageList)
        {
            int imageListIdx = IndexInImageList(imageName, ImageList);
            if (imageListIdx < 0 || gridData is null)
                return gridData?.Rows.Count ?? 0;

            // Scan rows in order; stop at the first row whose image comes AFTER
            // imageName in the ImageList — insert before that row.
            for (int r = 0; r < gridData.Rows.Count; r++)
            {
                string rowImage = gridData.Rows[r]["ImageName"]?.ToString() ?? string.Empty;
                int rowIdx = IndexInImageList(rowImage, ImageList);
                if (rowIdx > imageListIdx)
                    return r;
            }

            return gridData.Rows.Count; // Append at end
        }

        /// <summary>
        /// Returns the index after the last existing row for <paramref name="imageName"/>.
        /// Used by the Add Row button so manually added rows stay grouped with
        /// their image.
        /// </summary>
        public int InsertionIndexAfterLastRowOf(string imageName, DataTable gridData, ObservableCollection<ImageItem> ImageList)
        {
            if (gridData is null) return 0;

            int lastRow = -1;
            if (gridData.Columns.Contains("ImageName"))
            {
                for (int i = 0; i < gridData.Rows.Count; i++)
                {
                    if (gridData.Rows[i]["ImageName"]?.ToString() == imageName)
                        lastRow = i;
                }
            }

            return lastRow >= 0 ? lastRow + 1 : FirstInsertionIndexFor(imageName, gridData, ImageList);
        }

        public int IndexInImageList(string fileName, ObservableCollection<ImageItem> ImageList)
        {
            for (int i = 0; i < ImageList.Count; i++)
                if (ImageList[i].FileName == fileName) return i;
            return -1;
        }
    }
}
