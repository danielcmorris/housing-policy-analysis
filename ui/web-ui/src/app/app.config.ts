import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAuth0, authHttpInterceptorFn } from '@auth0/auth0-angular';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    // Attach Auth0 access tokens to API calls that can use them. allowAnonymous
    // lets the same requests go out without a token when nobody is signed in —
    // the API decides (and allows everything while Auth:Enabled=false).
    provideHttpClient(withInterceptors([authHttpInterceptorFn])),
    provideAuth0({
      domain: 'urbanpolicy.us.auth0.com',
      clientId: 'YgU1b1hokXlL7pEMBwL9q1oePPaPoGEe',
      cacheLocation: 'localstorage',
      authorizationParams: {
        redirect_uri: window.location.origin,
        audience: 'https://api.urbanpolicy.us',
      },
      httpInterceptor: {
        allowedList: [
          {
            uriMatcher: (uri) => uri.includes('/api/admin/') || uri.includes('/api/users/'),
            allowAnonymous: true,
          },
        ],
      },
    }),
  ]
};
