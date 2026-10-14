import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";

// "server-only" throws outside a server bundle; unit tests import server helpers directly.
vi.mock("server-only", () => ({}));

afterEach(cleanup);
