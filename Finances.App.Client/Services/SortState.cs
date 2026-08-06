namespace Finances.App.Client.Services;

/// <summary>
/// Column sort state shared by the sortable tables (History, Sales).
/// </summary>
public sealed class SortState
{
    public SortState(string defaultColumn, bool ascending = false)
    {
        Column = defaultColumn;
        Ascending = ascending;
    }

    public string Column { get; private set; }
    public bool Ascending { get; private set; }

    public void Toggle(string column)
    {
        if (Column == column)
        {
            Ascending = !Ascending;
        }
        else
        {
            Column = column;
            Ascending = true;
        }
    }

    public string Icon(string column)
    {
        return Column != column ? "" : Ascending ? "▲" : "▼";
    }

    public string AriaSort(string column)
    {
        return Column != column ? "none" : Ascending ? "ascending" : "descending";
    }
}
