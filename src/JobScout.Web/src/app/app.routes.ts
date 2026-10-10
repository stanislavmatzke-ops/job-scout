import { Routes } from '@angular/router';
import { memberGuard, signedInGuard, signedOutGuard } from './core/guards';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [signedOutGuard],
    title: 'Přihlášení – Job Scout',
    loadComponent: () => import('./pages/login/login.page').then(m => m.LoginPage),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [signedInGuard],
    children: [
      {
        path: 'access',
        title: 'Přístup – Job Scout',
        loadComponent: () => import('./pages/access/access.page').then(m => m.AccessPage),
      },
      {
        path: '',
        canActivate: [memberGuard],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'matches' },
          {
            path: 'matches',
            title: 'Nabídky – Job Scout',
            loadComponent: () => import('./pages/matches/matches.page').then(m => m.MatchesPage),
          },
          {
            path: 'profile',
            title: 'Profil – Job Scout',
            loadComponent: () => import('./pages/profile/profile.page').then(m => m.ProfilePage),
          },
          {
            path: 'matches/:id',
            title: 'Detail nabídky – Job Scout',
            loadComponent: () => import('./pages/match-detail/match-detail.page').then(m => m.MatchDetailPage),
          },
        ],
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
