import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/types";
import { renderIntl } from "@/test/intl";

const sendJson = vi.fn();
const sendForm = vi.fn();
const refresh = vi.fn();
vi.mock("@/lib/api/client", () => ({ sendJson: (...a: unknown[]) => sendJson(...a), sendForm: (...a: unknown[]) => sendForm(...a), api: {}, call: vi.fn() }));
vi.mock("@/i18n/navigation", () => ({ useRouter: () => ({ refresh, push: vi.fn() }), Link: "a" }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { CmsManager, type CmsLabels } from "./cms-manager";

const labels: CmsLabels = {
  add: "Add a step", edit: "Edit", save: "Save changes", create: "Create", cancel: "Cancel", delete: "Delete", deleteTitle: "Delete this step?",
  saved: "Saved.", deleted: "Deleted.", empty: "No steps yet.", yes: "Yes", no: "No", actions: "Actions", required: "This field is required.", currentFile: "Current file",
};
const fields = [
  { name: "titleEn", label: "Title (English)", kind: "text" as const, required: true },
  { name: "titleAr", label: "Title (Arabic)", kind: "text" as const, required: true, rtl: true },
  { name: "sortOrder", label: "Display order", kind: "number" as const },
  { name: "isActive", label: "Shown on the site", kind: "bool" as const },
];
const columns = [{ key: "titleEn", label: "Title (English)" }, { key: "isActive", label: "Shown", kind: "bool" as const }];
const rows = [{ id: 7, titleEn: "Register", titleAr: "سجّل", sortOrder: 2, isActive: true }];

const setup = (props: Partial<React.ComponentProps<typeof CmsManager>> = {}) =>
  renderIntl(<CmsManager path="/api/admin/how-to" labels={labels} fields={fields} columns={columns} rows={rows} titleKeys={["titleEn"]} defaults={{ sortOrder: "0", isActive: true }} {...props} />);

beforeEach(() => {
  sendJson.mockReset().mockResolvedValue({ id: 1 });
  sendForm.mockReset().mockResolvedValue({ id: 1 });
  refresh.mockReset();
});

describe("CmsManager", () => {
  it("lists rows and shows the empty text when there are none", () => {
    setup();
    expect(screen.getByRole("cell", { name: "Register" })).toBeInTheDocument();
    expect(screen.getByText("Yes")).toBeInTheDocument();
  });
  it("shows the empty state", () => {
    setup({ rows: [] });
    expect(screen.getByText("No steps yet.")).toBeInTheDocument();
  });

  it("hides 'add' for a single-record page once it has a record", () => {
    setup({ single: true });
    expect(screen.queryByRole("button", { name: "Add a step" })).not.toBeInTheDocument();
  });

  it("blocks an empty submit, names the required fields and focuses the first one", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: "Add a step" }));
    const dlg = screen.getByRole("dialog");
    await user.click(within(dlg).getByRole("button", { name: "Create" }));
    expect(within(dlg).getAllByRole("alert")).toHaveLength(2);
    expect(within(dlg).getByLabelText(/Title \(English\)/)).toHaveFocus();
    expect(sendJson).not.toHaveBeenCalled();
  });

  it("creates with numbers as numbers and refreshes the list", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: "Add a step" }));
    const dlg = screen.getByRole("dialog");
    await user.type(within(dlg).getByLabelText(/Title \(English\)/), "Bid");
    await user.type(within(dlg).getByLabelText(/Title \(Arabic\)/), "زايد");
    await user.clear(within(dlg).getByLabelText("Display order"));
    await user.type(within(dlg).getByLabelText("Display order"), "3");
    await user.click(within(dlg).getByRole("button", { name: "Create" }));
    await waitFor(() => expect(sendJson).toHaveBeenCalledWith("POST", "/api/admin/how-to", { titleEn: "Bid", titleAr: "زايد", sortOrder: 3, isActive: true }));
    expect(refresh).toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("edits an existing record with PUT to its id, prefilled", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: "Edit: Register" }));
    const dlg = screen.getByRole("dialog");
    expect(within(dlg).getByLabelText(/Title \(English\)/)).toHaveValue("Register");
    await user.type(within(dlg).getByLabelText(/Title \(English\)/), " now");
    await user.click(within(dlg).getByRole("button", { name: "Save changes" }));
    await waitFor(() => expect(sendJson).toHaveBeenCalledWith("PUT", "/api/admin/how-to/7", expect.objectContaining({ titleEn: "Register now", sortOrder: 2 })));
  });

  it("uses multipart with the chosen files for endpoints that take images", async () => {
    const user = userEvent.setup();
    setup({ multipart: true, fields: [...fields, { name: "image", label: "Image", kind: "file", current: "imageFileId" }] });
    await user.click(screen.getByRole("button", { name: "Add a step" }));
    const dlg = screen.getByRole("dialog");
    await user.type(within(dlg).getByLabelText(/Title \(English\)/), "A");
    await user.type(within(dlg).getByLabelText(/Title \(Arabic\)/), "ب");
    const file = new File(["x"], "pic.png", { type: "image/png" });
    await user.upload(dlg.querySelector('input[type="file"]') as HTMLInputElement, file);
    await user.click(within(dlg).getByRole("button", { name: "Create" }));
    await waitFor(() => expect(sendForm).toHaveBeenCalledWith("POST", "/api/admin/how-to", expect.objectContaining({ titleEn: "A" }), { image: file }));
  });

  it("keeps the dialog open and shows the API's message when saving fails", async () => {
    const user = userEvent.setup();
    sendJson.mockRejectedValueOnce(new ApiError(400, "cms.title_required"));
    setup();
    await user.click(screen.getByRole("button", { name: "Add a step" }));
    const dlg = screen.getByRole("dialog");
    await user.type(within(dlg).getByLabelText(/Title \(English\)/), "A");
    await user.type(within(dlg).getByLabelText(/Title \(Arabic\)/), "ب");
    await user.click(within(dlg).getByRole("button", { name: "Create" }));
    expect(await within(dlg).findByText("Enter a title.")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("asks before deleting, then sends DELETE", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: "Delete: Register" }));
    expect(sendJson).not.toHaveBeenCalled();
    const dlg = screen.getByRole("dialog");
    expect(within(dlg).getByText(/Register/)).toBeInTheDocument();
    await user.click(within(dlg).getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(sendJson).toHaveBeenCalledWith("DELETE", "/api/admin/how-to/7"));
  });
});
