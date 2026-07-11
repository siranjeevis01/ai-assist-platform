import { ApplicationConfig, provideZoneChangeDetection, APP_INITIALIZER } from '@angular/core';
import { provideRouter, withComponentInputBinding, withRouterConfig } from '@angular/router';
import { provideHttpClient, withInterceptors, withFetch } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { retryInterceptor } from './core/interceptors/retry.interceptor';
import { PushNotificationService } from './core/services/push-notification.service';

function initPushNotifications(push: PushNotificationService) {
  return () => {
    const token = localStorage.getItem('access_token');
    if (token) {
      push.initialize();
    }
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding(), withRouterConfig({ paramsInheritanceStrategy: 'always' })),
    provideHttpClient(withFetch(), withInterceptors([retryInterceptor, authInterceptor])),
    {
      provide: APP_INITIALIZER,
      useFactory: initPushNotifications,
      deps: [PushNotificationService],
      multi: true,
    },
  ]
};
