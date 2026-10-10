import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-matches-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1>Nabídky</h1><p>Přehled vašich nabídek připravujeme (krok 6b).</p>`,
})
export class MatchesPage {}
