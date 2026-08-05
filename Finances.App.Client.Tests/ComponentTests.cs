using Bunit;
using Finances.App.Client.Pages;
using Finances.App.Client.Services;
using Finances.App.Client.Services.Storage;
using Finances.App.Client.Shared;
using Finances.App.Client.Tests.TestDoubles;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Finances.App.Client.Tests;

public class ModalDialogTests : BunitContext
{
    public ModalDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Hidden_modal_renders_nothing()
    {
        var cut = Render<ModalDialog>(ps => ps.Add(p => p.IsVisible, false));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void Visible_modal_has_dialog_semantics_and_labelled_title()
    {
        var cut = Render<ModalDialog>(ps => ps
            .Add(p => p.IsVisible, true)
            .Add(p => p.Title, "Edit Thing"));

        var dialog = cut.Find("[role=dialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        var titleId = dialog.GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrEmpty(titleId));
        Assert.Equal("Edit Thing", cut.Find($"#{titleId}").TextContent);
        Assert.NotNull(cut.Find("button[aria-label=Close]"));
    }

    [Fact]
    public void Escape_key_and_close_button_invoke_on_close()
    {
        var closeCount = 0;
        var cut = Render<ModalDialog>(ps => ps
            .Add(p => p.IsVisible, true)
            .Add(p => p.Title, "T")
            .Add(p => p.OnClose, () => closeCount++));

        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.Find("button[aria-label=Close]").Click();

        Assert.Equal(2, closeCount);
    }
}

public class ConfirmDialogTests : BunitContext
{
    public ConfirmDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Confirm_and_cancel_invoke_their_callbacks()
    {
        var confirmed = 0;
        var cancelled = 0;
        var cut = Render<ConfirmDialog>(ps => ps
            .Add(p => p.IsVisible, true)
            .Add(p => p.ConfirmText, "Delete")
            .Add(p => p.OnConfirm, () => confirmed++)
            .Add(p => p.OnCancel, () => cancelled++));

        cut.Find(".btn-danger").Click();
        cut.Find(".btn-secondary").Click();
        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, confirmed);
        Assert.Equal(2, cancelled);
    }
}

public class ToastHostTests : BunitContext
{
    [Fact]
    public void Showing_a_toast_renders_it_in_the_host()
    {
        var toastService = new ToastService();
        Services.AddSingleton(toastService);

        var cut = Render<Toast>();
        toastService.Show("Saved!", ToastLevel.Success);

        cut.WaitForAssertion(() => Assert.Contains("Saved!", cut.Find(".toast-item").TextContent));
    }
}

public class WorkersPageTests : BunitContext
{
    public WorkersPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var store = new LocalFinanceStore(TestData.CreateSeededStorage());
        Services.AddSingleton<IFinanceStore>(store);
        Services.AddSingleton(new ToastService());
    }

    [Fact]
    public void Workers_page_lists_seeded_workers()
    {
        var cut = Render<Workers>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));
        Assert.Contains("Jan Kowalski", cut.Markup);
    }

    [Fact]
    public void Adding_a_worker_through_the_modal_updates_the_table()
    {
        var cut = Render<Workers>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));

        cut.FindAll("button").First(b => b.TextContent.Contains("Add Worker")).Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role=dialog]")));

        cut.Find("#worker-name").Change("Test Person");
        cut.Find("#worker-commission").Change("35");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));
        Assert.Contains("Test Person", cut.Markup);
    }

    [Fact]
    public async Task Deleting_a_worker_with_records_shows_the_real_refusal_reason()
    {
        // Give worker 1 a service record so the delete is refused.
        var storage = TestData.CreateSeededStorage();
        var store = new LocalFinanceStore(storage);
        await store.AddServiceRecordAsync(new Finances.App.Shared.ServiceRecord
        {
            WorkerId = 1,
            ServiceId = 1,
            DatePerformed = DateTime.Today,
            AmountPaid = 10,
            CommissionPercentageApplied = 50
        });

        var toastService = new ToastService();
        Services.AddSingleton<IFinanceStore>(store);
        Services.AddSingleton(toastService);

        var cut = Render<Workers>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));

        // Delete specifically the worker that has records (rows are sorted by name).
        var row = cut.FindAll("tbody tr").First(r => r.TextContent.Contains("Jan Kowalski"));
        row.QuerySelector("button.btn-outline-danger")!.Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role=dialog]")));
        cut.FindAll("button").First(b => b.TextContent == "Delete" && b.ClassList.Contains("btn-danger")).Click();

        cut.WaitForAssertion(() =>
            Assert.Contains(toastService.Toasts, t => t.Message.Contains("existing service records")));
        Assert.Equal(3, cut.FindAll("tbody tr").Count);
    }
}
