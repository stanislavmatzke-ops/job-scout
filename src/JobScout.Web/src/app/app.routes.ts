import { Routes } from '@angular/router';
import { adminGuard, memberGuard, signedInGuard, signedOutGuard } from './core/guards';
import { ShellComponent } from './layout/shell.component';

/** title = a key of t().titles – I18nTitleStrategy makes "Nabídky – Job Scout" / "Jobs – Job Scout" of it. */
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [signedOutGuard],
    title: 'login',
    loadComponent: () => import('./pages/login/login.page').then(m => m.LoginPage),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [signedInGuard],
    children: [
      {
        path: 'access',
        title: 'access',
        loadComponent: () => import('./pages/access/access.page').then(m => m.AccessPage),
      },
      {
        path: '',
        canActivate: [memberGuard],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'matches' },
          {
            path: 'matches',
            title: 'matches',
            loadComponent: () => import('./pages/matches/matches.page').then(m => m.MatchesPage),
          },
          {
            path: 'profile',
            title: 'profile',
            loadComponent: () => import('./pages/profile/profile.page').then(m => m.ProfilePage),
          },
          {
            path: 'feedback',
            title: 'feedback',
            loadComponent: () => import('./pages/feedback/feedback.page').then(m => m.FeedbackPage),
          },
          {
            path: 'admin',
            canActivate: [adminGuard],
            title: 'admin',
            loadComponent: () => import('./pages/admin/admin.page').then(m => m.AdminPage),
          },
          {
            path: 'matches/:id',
            title: 'matchDetail',
            loadComponent: () => import('./pages/match-detail/match-detail.page').then(m => m.MatchDetailPage),
          },
          {
            path: '**',
            title: 'notFound',
            loadComponent: () => import('./pages/not-found/not-found.page').then(m => m.NotFoundPage),
          },
        ],
      },
    ],
  },
];
