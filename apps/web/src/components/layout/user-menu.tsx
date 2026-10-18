"use client";

import { ChevronDown, Heart, LayoutDashboard, LogOut, Settings, Store, UserRound, Wallet, Eye } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Menu, MenuItem, MenuLabel, MenuSeparator } from "@/components/ui/overlay";
import { useSession } from "@/components/providers";
import { Link, useRouter } from "@/i18n/navigation";
import { api, call, setCsrfToken } from "@/lib/api/client";

export { NotificationBell } from "./notification-popover";

export function useSignOut() {
  const router = useRouter();
  return async () => {
    const r = await call(() => api.POST("/api/auth/logout"));
    setCsrfToken(r?.csrfToken);
    router.push("/");
    router.refresh();
  };
}

export function UserMenu() {
  const t = useTranslations("common.user");
  const user = useSession();
  const signOut = useSignOut();
  if (!user) return null;
  return (
    <Menu
      trigger={
        <Button variant="secondary" className="h-11 gap-1.5 px-3" aria-label={t("account")}>
          <UserRound className="h-5 w-5" aria-hidden />
          <span className="hidden max-w-36 truncate sm:inline">{user.fullName}</span>
          <ChevronDown className="h-4 w-4" aria-hidden />
        </Button>
      }
    >
      <MenuLabel>{user.email}</MenuLabel>
      {user.role === "Admin" && (
        <MenuItem asChild>
          <Link href="/admin">
            <LayoutDashboard className="h-4 w-4" /> {t("adminPanel")}
          </Link>
        </MenuItem>
      )}
      {user.publisherId != null && (
        <MenuItem asChild>
          <Link href="/seller">
            <Store className="h-4 w-4" /> {t("sellerPortal")}
          </Link>
        </MenuItem>
      )}
      {user.role !== "Admin" && (
        <>
          <MenuItem asChild>
            <Link href="/account">
              <LayoutDashboard className="h-4 w-4" /> {t("account")}
            </Link>
          </MenuItem>
          <MenuItem asChild>
            <Link href="/account/wallet">
              <Wallet className="h-4 w-4" /> {t("wallet")}
            </Link>
          </MenuItem>
          <MenuItem asChild>
            <Link href="/account/favorites">
              <Heart className="h-4 w-4" /> {t("favorites")}
            </Link>
          </MenuItem>
          <MenuItem asChild>
            <Link href="/account/watching">
              <Eye className="h-4 w-4" /> {t("watching")}
            </Link>
          </MenuItem>
          <MenuItem asChild>
            <Link href="/account/settings">
              <Settings className="h-4 w-4" /> {t("settings")}
            </Link>
          </MenuItem>
        </>
      )}
      <MenuSeparator />
      <MenuItem onSelect={() => void signOut()} danger>
        <LogOut className="h-4 w-4" /> {t("signOut")}
      </MenuItem>
    </Menu>
  );
}
