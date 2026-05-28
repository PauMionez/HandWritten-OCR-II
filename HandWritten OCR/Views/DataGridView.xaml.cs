using HandWritten_OCR.ViewModels;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace HandWritten_OCR.Views;

public partial class DataGridView : UserControl
{
    private MainViewModel? _vm;

    public DataGridView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.RequestCellFocus -= OnRequestCellFocus;

        _vm = e.NewValue as MainViewModel;

        if (_vm is not null)
            _vm.RequestCellFocus += OnRequestCellFocus;
    }

    private void OcrDataGrid_CurrentCellChanged(object sender, EventArgs e)
    {
        if (_vm is null) return;
        var grid = (DataGrid)sender;

        if (grid.CurrentCell.IsValid &&
            grid.CurrentCell.Item is DataRowView rowView &&
            grid.CurrentCell.Column?.Header is string colName)
        {
            int rowIndex = rowView.Row.Table.Rows.IndexOf(rowView.Row);
            _vm.SetSelectedCell(colName, rowIndex);
        }
    }

    private void OnRequestCellFocus(int rowIndex, int colIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (OcrDataGrid.Items.Count <= rowIndex) return;
            if (OcrDataGrid.Columns.Count <= colIndex) return;

            var item = OcrDataGrid.Items[rowIndex];
            var column = OcrDataGrid.Columns[colIndex];
            OcrDataGrid.CurrentCell = new DataGridCellInfo(item, column);
            OcrDataGrid.ScrollIntoView(item, column);
        });
    }
}
