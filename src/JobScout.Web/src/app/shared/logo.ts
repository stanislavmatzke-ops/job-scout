import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The Job Scout mark: a magnifying glass (we search for you) with a spark inside the lens (the AI looks closer).
 * Same drawing as public/favicon.svg – keep the two in sync.
 */
@Component({
  selector: 'app-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 32 32" role="img" aria-label="Job Scout">
      <rect width="32" height="32" rx="8" fill="#0f6cbd" />
      <circle cx="14" cy="14" r="7.5" fill="none" stroke="#fff" stroke-width="2.8" />
      <path d="M19.6 19.6 25 25" stroke="#fff" stroke-width="3.2" stroke-linecap="round" />
      <path d="M14 9.6 15.1 12.9 18.4 14 15.1 15.1 14 18.4 12.9 15.1 9.6 14 12.9 12.9Z" fill="#c9c2ff" />
    </svg>
  `,
  styles: `:host { display: inline-flex; } svg { display: block; }`,
})
export class Logo {
  readonly size = input(32);
}
