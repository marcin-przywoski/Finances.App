using Finances.App.Client.Models;
using Finances.App.Client.Services;
using Microsoft.AspNetCore.Components;

namespace Finances.App.Client.Pages;

/// <summary>
/// Shared state and control flow for the three catalog CRUD pages
/// (Workers, Services, Products). Markup — table columns and form fields —
/// stays in each page; this base owns the modal/save/delete lifecycle that
/// was previously copy-pasted across them.
/// </summary>
public abstract class CrudPageBase<TItem> : ComponentBase where TItem : class, new()
{
    [Inject] protected ToastService Toast { get; set; } = default!;

    protected List<TItem>? items;
    protected TItem formModel = new();
    protected TItem? deleteTarget;
    protected bool showModal;
    protected bool isEditing;
    protected bool saving;
    protected bool showDeleteConfirm;
    protected int editId;

    /// <summary>Singular display name used in toasts, e.g. "Worker".</summary>
    protected abstract string EntityName { get; }

    protected abstract Task<IReadOnlyList<TItem>> LoadItemsAsync();
    protected abstract Task AddItemAsync(TItem item);
    protected abstract Task UpdateItemAsync(int id, TItem item);
    protected abstract Task<DeleteResult> DeleteItemAsync(TItem item);
    protected abstract int GetId(TItem item);
    protected abstract TItem CloneItem(TItem item);

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
    }

    protected async Task ReloadAsync()
    {
        items = (await LoadItemsAsync()).ToList();
    }

    protected void OpenAddModal()
    {
        formModel = new TItem();
        isEditing = false;
        showModal = true;
    }

    protected void OpenEditModal(TItem item)
    {
        formModel = CloneItem(item);
        editId = GetId(item);
        isEditing = true;
        showModal = true;
    }

    protected void CloseModal() => showModal = false;

    protected async Task HandleSubmit()
    {
        saving = true;
        try
        {
            if (isEditing)
            {
                await UpdateItemAsync(editId, formModel);
                Toast.Show($"{EntityName} updated");
            }
            else
            {
                await AddItemAsync(formModel);
                Toast.Show($"{EntityName} added");
            }
            await ReloadAsync();
            showModal = false;
        }
        catch (Exception ex)
        {
            Toast.Show(UserFacingError.Message(ex), ToastLevel.Error);
        }
        finally
        {
            saving = false;
        }
    }

    protected void ConfirmDelete(TItem item)
    {
        deleteTarget = item;
        showDeleteConfirm = true;
    }

    protected async Task HandleDelete()
    {
        if (deleteTarget is null)
        {
            return;
        }

        try
        {
            var deleted = CloneItem(deleteTarget);
            var result = await DeleteItemAsync(deleteTarget);
            if (result.Success)
            {
                ShowUndoToast($"{EntityName} deleted", deleted);
            }
            else
            {
                Toast.Show(result.ErrorMessage ?? $"Cannot delete this {EntityName.ToLowerInvariant()}.", ToastLevel.Error);
            }
        }
        catch (Exception ex)
        {
            Toast.Show(UserFacingError.Message(ex), ToastLevel.Error);
        }

        showDeleteConfirm = false;
        deleteTarget = null;
        await ReloadAsync();
    }

    /// <summary>
    /// Deletable items are guaranteed unreferenced (referential-integrity
    /// checks refuse otherwise), so undo can simply re-add the clone. The
    /// restored item gets a fresh id, which nothing else points at.
    /// </summary>
    private void ShowUndoToast(string message, TItem deleted)
    {
        Toast.Show(message, ToastLevel.Success, "Undo", async () =>
        {
            await AddItemAsync(deleted);
            await InvokeAsync(async () =>
            {
                await ReloadAsync();
                StateHasChanged();
            });
        });
    }
}
