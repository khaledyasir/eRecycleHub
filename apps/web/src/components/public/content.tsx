import type { ReactNode } from "react";

/** Standard header + width for informational pages. */
export function ContentPage({ title, description, children, wide }: { title: string; description?: string; children: ReactNode; wide?: boolean }) {
  return (
    <div className="container-page py-10">
      <header className="mb-8 max-w-3xl">
        <h1 className="text-3xl font-semibold">{title}</h1>
        {description && <p className="mt-2 text-lg text-muted">{description}</p>}
      </header>
      <div className={wide ? undefined : "max-w-3xl"}>{children}</div>
    </div>
  );
}
