"use client";

import type { SelectHTMLAttributes } from "react";

/** A select that submits its GET form when changed (the form still works with a submit button when JavaScript is off). */
export function AutoSubmitSelect(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} onChange={(e) => e.currentTarget.form?.requestSubmit()} />;
}
