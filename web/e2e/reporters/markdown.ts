import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import type { FullResult, Reporter, TestCase, TestResult } from '@playwright/test/reporter';

interface RecordedTest {
  name: string;
  status: string;
  durationMs: number;
  retry: number;
  detail: string;
}

class MarkdownReporter implements Reporter {
  private readonly tests: RecordedTest[] = [];

  onTestEnd(test: TestCase, result: TestResult): void {
    this.tests.push({
      name: test.titlePath().filter(Boolean).join(' › '),
      status: result.status,
      durationMs: result.duration,
      retry: result.retry,
      detail:
        result.status === 'skipped'
          ? test.annotations.find((annotation) => annotation.type === 'skip')?.description ?? ''
          : '',
    });
  }

  async onEnd(result: FullResult): Promise<void> {
    const statuses = ['passed', 'failed', 'timedOut', 'skipped', 'interrupted'] as const;
    const counts = Object.fromEntries(statuses.map((status) => [
      status,
      this.tests.filter((test) => test.status === status).length,
    ])) as Record<(typeof statuses)[number], number>;
    const lines = [
      '# Playwright E2E test results',
      '',
      `- Run at (UTC): ${new Date().toISOString()}`,
      `- Overall status: **${result.status}**`,
      `- Runtime: ${process.version} (${process.platform})`,
      `- Tests: ${this.tests.length} total; ${counts.passed} passed; ${counts.failed} failed; ${counts.timedOut} timed out; ${counts.skipped} skipped; ${counts.interrupted} interrupted`,
      '',
      '| Result | Test | Duration | Retry | Details |',
      '|---|---|---:|---:|---|',
      ...this.tests
        .sort((left, right) => left.name.localeCompare(right.name))
        .map((test) =>
          `| ${test.status} | ${test.name.replaceAll('|', '\\|')} | ${(test.durationMs / 1000).toFixed(2)}s | ${test.retry} | ${test.detail.replaceAll('|', '\\|')} |`
        ),
      '',
      'Generated automatically by the Playwright test runner. See [README](./README.md) for setup and run instructions.',
      '',
    ];

    await writeFile(resolve(process.cwd(), 'e2e', 'TEST-RESULTS.md'), lines.join('\n'), 'utf8');
  }
}

export default MarkdownReporter;
