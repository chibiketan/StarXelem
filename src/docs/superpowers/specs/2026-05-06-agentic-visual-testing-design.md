---
name: Agentic Visual Testing Framework Design
description: Architecture for an autonomous visual testing system using three specialized agents.
type: design
---

# Design Spec: Agentic Visual Testing Framework

## Context
The goal is to create a robust, automated visual quality assurance system for the StarXelem application. Instead of traditional unit tests, this framework uses an agentic approach to compare the actual UI rendered by Avalonia against "ideal" visual references generated from HTML mockups. This allows for semantic validation of the design rather than just pixel-perfect matching.

## Architecture Overview

The system is composed of three specialized agents orchestrated via a CLI tool.

### 1. Agent A: The Pilot (Execution)
**Purpose:** To drive the application to a specific state and capture its visual output.
*   **Technology:** **Avalonia Headless** (In-process testing).
*   **Mechanism:** 
    *   Runs the application in a headless environment, bypassing the need for a physical window.
    *   Navigates through the UI by searching the Visual Tree (e.g., using `AutomationId`).
    *   Uses the internal Skia rendering engine to capture screenshots directly from the rendered controls/window.
*   **Input:** Target page/component name, configuration flags (`--mock-data`, `--test-mode`).
*   **Output:** A high-resolution `.png` of the actual application state.

### 2. Agent B: The Reference Generator (Truth)
**Purpose:** To create the "Golden Image" representing the intended design.
*   **Technology:** **Playwright** (Headless browser).
*   **Mechanism:** 
    *   Takes HTML/CSS mockups (provided by an LLM or designer).
    *   Renders them in a controlled headless environment.
    *   Captures a screenshot of the rendered component.
*   **Input:** HTML/CSS source files.
*   **Output:** A high-resolution `.png` representing the design truth.

### 3. Agent C: The Visual Judge (Analysis)
**Purpose:** To interpret visual discrepancies using semantic intelligence.
*   **Technology:** **Local Multimodal LLM** (e.g., LLaVA or Moondream via Ollama).
*   **Mechanism:** 
    *   Receives the "Actual" image and the "Reference" image.
    *   Performs a visual comparison.
    *   Generates a natural language report describing *why* the UI is non-compliant (e.g., "The button color is incorrect", "The icon is misaligned").
*   **Input:** Actual Image, Reference Image.
*   **Output:** A structured JSON/Text report containing the compliance status and semantic analysis.

## Reporting System

To make the results actionable for a developer, the CLI will generate a **Visual Audit Report**.

### Report Structure (HTML Format)
The report will be a standalone HTML file containing:
1.  **Summary Dashboard:** 
    *   Overall Status (PASS/FAIL).
    *   Total tests run, passed, and failed.
2.  **Comparison Gallery:**
    *   A side-by-side view of the **Reference Image** vs. the **Actual Image**.
    *   An **Overlay/Diff Image**: A third image showing the two layers superimposed with a color-coded heatmap (red for differences).
3.  **Semantic Analysis Log:**
    *   For every failure, the text output from Agent C (the LLM) explaining the nature of the error.
4.  **Metadata:** Timestamp, target component, and version info.

## Implementation Strategy

### Phase 1: Foundation & Headless Setup
*   Create a new test project using `Avalonia.Headless`.
*   Implement the CLI entry point to handle `--test-mode` and `--mock-data`.
*   Configure the DI container to allow swapping real services for mocks when the headless flag is present.

### Phase 2: Pilot & Reference Generation
*   Develop the "Pilot" logic using Avalonia Headless to navigate the Visual Tree via `AutomationId`.
*   Integrate `Playwright` to render HTML mockups and generate "Golden Images".

### Phase 3: Intelligence & Reporting
*   Integrate a local multimodal LLM (via Ollama) for semantic analysis.
*   Implement the HTML reporting engine with side-by-side comparison and heatmap overlays.
