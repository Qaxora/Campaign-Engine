import {
  BarChart3,
  Boxes,
  Building2,
  LayoutDashboard,
  Megaphone,
  Plug,
  Receipt,
  Settings,
  Sparkles,
  type LucideIcon,
} from "lucide-react";

export interface NavLink {
  title: string;
  href: string;
}

export interface NavSection {
  title: string;
  icon: LucideIcon;
  href?: string;
  children?: NavLink[];
}

export const navigation: NavSection[] = [
  { title: "Dashboard", icon: LayoutDashboard, href: "/" },
  {
    title: "Campaigns",
    icon: Megaphone,
    children: [
      { title: "All Campaigns", href: "/campaigns" },
      { title: "Create Campaign", href: "/campaigns/new" },
      { title: "Conflicts", href: "/campaigns/conflicts" },
    ],
  },
  {
    title: "Catalog",
    icon: Boxes,
    children: [
      { title: "Products", href: "/catalog/products" },
      { title: "Product Lists", href: "/catalog/lists" },
      { title: "Exclusions", href: "/catalog/exclusions" },
    ],
  },
  {
    title: "Sales",
    icon: Receipt,
    children: [
      { title: "Redemptions", href: "/sales/redemptions" },
      { title: "Transactions", href: "/sales/transactions" },
      { title: "Usage", href: "/sales/usage" },
    ],
  },
  { title: "Analytics", icon: BarChart3, href: "/analytics" },
  { title: "AI Assistant", icon: Sparkles, href: "/assistant" },
  {
    title: "Integrations",
    icon: Plug,
    children: [
      { title: "API Keys", href: "/integrations/api-keys" },
      { title: "Webhooks", href: "/integrations/webhooks" },
      { title: "Integration Guide", href: "/integrations/guide" },
    ],
  },
  {
    title: "Settings",
    icon: Settings,
    children: [
      { title: "Organization", href: "/settings/organization" },
      { title: "Stores", href: "/settings/stores" },
      { title: "Users", href: "/settings/users" },
    ],
  },
];

export const organizationIcon = Building2;

/** A link is active on its own path and below it, except the dashboard which is exact. */
export function isActive(pathname: string, href: string): boolean {
  if (href === "/") return pathname === "/";
  return pathname === href || pathname.startsWith(`${href}/`);
}

/** The most specific active link, so /campaigns/new does not also highlight /campaigns. */
export function activeHref(pathname: string): string | undefined {
  return navigation
    .flatMap((section) => (section.children ?? []).map((c) => c.href).concat(section.href ? [section.href] : []))
    .filter((href) => isActive(pathname, href))
    .sort((a, b) => b.length - a.length)[0];
}
