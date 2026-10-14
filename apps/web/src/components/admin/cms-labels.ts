import "server-only";
import { getTranslations } from "next-intl/server";
import type { CmsLabels } from "./cms-manager";

/** Labels shared by every CMS list; `ns` is the page's own section in cms.json (add, empty, deleteTitle, deleteBody?). */
export async function cmsLabels(ns: string): Promise<CmsLabels> {
  const [c, p] = await Promise.all([getTranslations("cms.common"), getTranslations(`cms.${ns}`)]);
  return {
    add: p("add"), edit: c("edit"), save: c("save"), create: c("create"), cancel: c("cancel"), delete: c("delete"),
    deleteTitle: p("deleteTitle"), deleteBody: p.has("deleteBody") ? p("deleteBody") : undefined,
    saved: c("saved"), deleted: c("deleted"), empty: p("empty"), yes: c("yes"), no: c("no"), actions: c("actions"),
    required: c("required"), currentFile: c("currentFile"),
  };
}
