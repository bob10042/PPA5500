using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Newton4thGui.ViewModels;

namespace Newton4thGui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }

    private void LoggedRowsGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (DataContext is not MainViewModel vm) return;

        vm.LoggedRows.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                grid.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    if (grid.Items.Count > 0) grid.ScrollIntoView(grid.Items[grid.Items.Count - 1]!);
                }));
            }
        };
    }
}
