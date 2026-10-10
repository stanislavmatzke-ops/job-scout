import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../../core/auth.service';
import { ProfileService, cvProblem } from '../../core/profile.service';
import { GrammaticalGender, ProfileView, Seniority } from '../../core/profile.models';
import { aiErrorMessage, employmentLabel, problemMessage, remoteLabel } from '../../core/match-format';

const SENIORITY: Record<Seniority, string> = {
  Junior: 'Junior', Medior: 'Medior', Senior: 'Senior', Lead: 'Vedoucí / lead', Expert: 'Expert',
};

type Work = 'cv' | 'notes';

@Component({
  selector: 'app-profile-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, RouterLink, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatRadioModule, MatChipsModule, MatProgressBarModule,
  ],
  template: `
    <h1>Profil</h1>

    @if (loadError()) { <p class="error">Profil se nepodařilo načíst. Zkuste prosím stránku obnovit.</p> }

    @if (view(); as v) {
      @if (!aiEnabled()) {
        <p class="ai-off">Nahrání životopisu a změna poznámek spouští analýzu pomocí AI – u pozastaveného účtu jsou vypnuté.</p>
      }

      @if (v.outdatedEvaluations > 0) {
        <p class="info">
          <mat-icon>info</mat-icon>
          Nabídek čekajících na rozhodnutí, které byly ohodnoceny podle starší verze profilu: <strong>{{ v.outdatedEvaluations }}</strong>.
          V jejich <a routerLink="/matches">detailu</a> je můžete nechat přehodnotit.
        </p>
      }

      <div class="grid">
        <section class="column">
          <!-- ===== CV ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>Životopis</mat-card-title></mat-card-header>
            @if (working() === 'cv') { <mat-progress-bar mode="indeterminate" /> }
            <mat-card-content>
              @if (working() === 'cv') {
                <p class="muted">Analyzuji životopis – obvykle do 30 sekund…</p>
              } @else if (v.version) {
                <p>
                  <mat-icon class="inline">description</mat-icon> <strong>{{ v.cvFileName }}</strong><br />
                  <span class="muted">Profil verze {{ v.version }}, vyhodnocen {{ v.analyzedAt | date: 'd. M. y H:mm' }}</span>
                </p>
                @if (!v.hasCv) {
                  <p class="info">
                    <mat-icon>info</mat-icon>
                    Profil vznikl dříve, než jsme začali ukládat soubor životopisu. Pro úpravu poznámek prosím
                    nahrajte životopis znovu – pokud je stejný, profil se nezmění a nic se nepřepočítává.
                  </p>
                }
              } @else {
                <h3>Začněte nahráním životopisu</h3>
                <p>
                  Z životopisu a vašich poznámek AI sestaví profil – co umíte, jakou práci hledáte a kde.
                  Podle něj pak každé ráno vyhledáme nové nabídky, ohodnotíme je a k nejvhodnějším připravíme návrh
                  průvodního dopisu. Nic se neodesílá – o každé reakci rozhodujete vy.
                </p>
              }
              @if (fileError(); as message) { <p class="error">{{ message }}</p> }
              <p class="muted small">PDF, Markdown nebo text, nejvýš 5 MB. Kontaktní údaje z životopisu do profilu neukládáme.</p>
            </mat-card-content>
            <mat-card-actions>
              <input #file type="file" hidden accept=".pdf,.md,.txt" (change)="onFile(file)" />
              <button mat-flat-button (click)="file.click()" [disabled]="!aiEnabled() || working() !== null">
                <mat-icon>upload_file</mat-icon> {{ v.hasCv ? 'Nahrát nový životopis' : 'Nahrát životopis' }}
              </button>
            </mat-card-actions>
          </mat-card>

          <!-- ===== Notes ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>Poznámky a preference</mat-card-title></mat-card-header>
            @if (working() === 'notes') { <mat-progress-bar mode="indeterminate" /> }
            <mat-card-content>
              <p class="muted small">
                Co v životopisu není: jakou práci hledáte, kde, kolik chcete vydělávat, co vás nezajímá.
                Například: „Chci pracovat převážně z domova, nejvýš 1 den v kanceláři v Brně. Minimálně 95 000 Kč hrubého.
                Nechci projekty, kde se jen udržuje starý systém.“
              </p>
              <mat-form-field appearance="outline" class="full">
                <mat-label>Poznámky</mat-label>
                <textarea matInput rows="7" name="notes" maxlength="4000" [(ngModel)]="notes"
                          [disabled]="!v.hasCv || working() !== null"></textarea>
                @if (!v.hasCv) {
                  <mat-hint>Poznámky se vyhodnocují spolu se životopisem – {{ v.version ? 'nahrajte ho prosím znovu' : 'nejprve ho prosím nahrajte' }}.</mat-hint>
                }
              </mat-form-field>
            </mat-card-content>
            <mat-card-actions>
              <button mat-stroked-button (click)="saveNotes()"
                      [disabled]="!v.hasCv || !aiEnabled() || working() !== null || notes.trim() === (v.notes ?? '')">
                Uložit a znovu vyhodnotit profil
              </button>
            </mat-card-actions>
          </mat-card>

          <!-- ===== Gender ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>Tvary v průvodních dopisech</mat-card-title></mat-card-header>
            <mat-card-content>
              <p class="muted small">Čeština potřebuje rod („rád“ / „ráda“). Neuhadujeme ho ze jména – zvolte prosím sami.</p>
              <mat-radio-group class="radios" [ngModel]="v.gender" (ngModelChange)="saveGender($event)" name="gender">
                <mat-radio-button value="Masculine">Mužský rod (rád, zvyklý)</mat-radio-button>
                <mat-radio-button value="Feminine">Ženský rod (ráda, zvyklá)</mat-radio-button>
                <mat-radio-button value="Unspecified">Neuvádět (dopis se rodovým tvarům vyhne)</mat-radio-button>
              </mat-radio-group>
            </mat-card-content>
          </mat-card>
        </section>

        <!-- ===== What the AI made of it ===== -->
        <aside class="column">
          @if (v.profile; as p) {
            <mat-card appearance="outlined">
              <mat-card-header>
                <mat-card-title>Jak vás vidí AI</mat-card-title>
                <mat-card-subtitle>Podle tohoto profilu hledáme a hodnotíme nabídky</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <p class="headline">{{ p.headline }}</p>
                <p class="muted">{{ seniority(p.seniority) }} · praxe {{ p.yearsOfExperience }} let</p>

                <h3>Cílové role</h3>
                <mat-chip-set>@for (x of p.targetRoles; track x) { <mat-chip>{{ x }}</mat-chip> }</mat-chip-set>

                <h3>Hlavní dovednosti</h3>
                <mat-chip-set>@for (x of p.coreSkills; track x) { <mat-chip>{{ x }}</mat-chip> }</mat-chip-set>

                @if (p.domains.length) {
                  <h3>Obory</h3>
                  <p>{{ p.domains.join(', ') }}</p>
                }

                <h3>Preference</h3>
                <dl class="facts">
                  <dt>Práce z domova</dt><dd>{{ remoteLabel(p.preferences.remoteWork) }}</dd>
                  <dt>Forma spolupráce</dt>
                  <dd>{{ p.preferences.employmentTypes.length ? employmentList(p.preferences.employmentTypes) : 'Neuvedeno' }}</dd>
                  <dt>Minimální mzda</dt>
                  <dd>{{ p.preferences.minMonthlySalaryCzk ? (p.preferences.minMonthlySalaryCzk | number) + ' Kč' : 'Neuvedena' }}</dd>
                  <dt>Kde</dt>
                  <dd>
                    {{ p.search.homeLocation || 'Neuvedeno' }}@if (p.search.commuteRadiusKm) { + {{ p.search.commuteRadiusKm }} km}
                    @if (p.search.extraLocations.length) { , také {{ p.search.extraLocations.join(', ') }} }
                  </dd>
                </dl>
                @if (p.preferences.priorities.length) {
                  <h3>Co je pro vás důležité</h3>
                  <ol class="plain">@for (x of p.preferences.priorities; track $index) { <li>{{ x }}</li> }</ol>
                }
                @if (p.preferences.avoid.length) {
                  <h3>Čemu se vyhnout</h3>
                  <ul class="plain">@for (x of p.preferences.avoid; track $index) { <li>{{ x }}</li> }</ul>
                }

                <h3>Hledáme jako</h3>
                <p>{{ p.search.professions.join(', ') }}</p>
                <p class="muted small">Nesedí něco? Doplňte to do poznámek – profil se podle nich znovu vyhodnotí.</p>
              </mat-card-content>
            </mat-card>
          }
        </aside>
      </div>
    }
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; align-items: start; }
    .column { display: flex; flex-direction: column; gap: 16px; min-width: 0; }
    .full { width: 100%; }
    .small { font-size: 0.85em; }
    .inline { vertical-align: middle; }
    .ai-off, .info { padding: 8px 12px; border-radius: 8px; background: var(--mat-sys-surface-container-high); }
    .info { display: flex; gap: 8px; align-items: center; }
    .headline { font-size: 1.1rem; font-weight: 500; margin-bottom: 4px; }
    h3 { margin: 16px 0 6px; font-size: 1rem; }
    .facts { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0; }
    .facts dt { opacity: 0.7; }
    .facts dd { margin: 0; }
    .plain { margin: 0; padding-left: 20px; }
    .radios { display: flex; flex-direction: column; gap: 4px; }
    @media (max-width: 900px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class ProfilePage implements OnInit {
  private readonly profiles = inject(ProfileService);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly view = signal<ProfileView | null>(null);
  protected readonly loadError = signal(false);
  protected readonly working = signal<Work | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly aiEnabled = computed(() => this.auth.me()?.access === 'Approved');

  protected notes = '';

  protected readonly remoteLabel = remoteLabel;
  protected readonly seniority = (s: Seniority) => SENIORITY[s] ?? s;
  protected readonly employmentList = (types: Parameters<typeof employmentLabel>[0][]) => types.map(employmentLabel).join(', ');

  async ngOnInit(): Promise<void> {
    try {
      this.show(await this.profiles.get());
    } catch {
      this.loadError.set(true);
    }
  }

  protected async onFile(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';                                       // choosing the same file again fires (change) again
    if (!file) return;

    const problem = cvProblem(file);
    this.fileError.set(problem);
    if (problem) return;

    await this.analyze('cv', () => this.profiles.uploadCv(file));
  }

  protected async saveNotes(): Promise<void> {
    await this.analyze('notes', () => this.profiles.updateNotes(this.notes.trim() || null));
  }

  protected async saveGender(gender: GrammaticalGender): Promise<void> {
    try {
      await this.profiles.updateGender(gender);
      this.view.update(v => (v ? { ...v, gender } : v));
      this.toast('Nastavení bylo uloženo.');
    } catch (error) {
      this.toast(problemMessage(error, 'Nastavení se nepodařilo uložit.'));
    }
  }

  /** CV upload and notes both end in an AI analysis – unless nothing changed (the server compares a hash). */
  private async analyze(work: Work, call: () => Promise<ProfileView>): Promise<void> {
    if (this.working()) return;
    const before = this.view()?.version ?? null;
    this.working.set(work);
    try {
      const after = await call();
      this.show(after);
      this.toast(after.version === before
        ? 'Nic se nezměnilo – profil zůstává stejný.'
        : 'Profil byl vyhodnocen. Nové nabídky budeme hledat a hodnotit podle něj.');
    } catch (error) {
      this.toast(aiErrorMessage(error), 6000);
    } finally {
      this.working.set(null);
    }
  }

  private show(view: ProfileView): void {
    this.notes = view.notes ?? '';
    this.view.set(view);
  }

  private toast(message: string, duration = 4000): void {
    this.snackBar.open(message, undefined, { duration });
  }
}
