import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// inject() must run before the first await – afterwards the injection context is gone

/** Signed in at all (also Pending). */
export const signedInGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const me = await auth.load();
  return me ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Approved or Suspended – a Pending user goes to "request access" (ADR-0017). */
export const memberGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const me = await auth.load();
  if (!me) return router.createUrlTree(['/login']);
  return me.access === 'Pending' ? router.createUrlTree(['/access']) : true;
};

export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const me = await auth.load();
  return me?.isAdmin ? true : router.createUrlTree(['/']);
};

/** The login page makes no sense when already signed in. */
export const signedOutGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return (await auth.load()) ? router.createUrlTree(['/']) : true;
};
