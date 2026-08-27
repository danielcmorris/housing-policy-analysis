import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then(m => m.HomePage) },
  { path: 'studies', loadComponent: () => import('./pages/library/library').then(m => m.LibraryPage) },
  { path: 'studies/:ref', loadComponent: () => import('./pages/study/study').then(m => m.StudyPage) },
  { path: 'bills/:id', loadComponent: () => import('./pages/bill-review/bill-review').then(m => m.BillReviewPage) },
  { path: 'assistant', loadComponent: () => import('./pages/assistant/assistant').then(m => m.AssistantPage) },
  { path: 'commons', loadComponent: () => import('./pages/commons/commons').then(m => m.CommonsPage) },
  { path: 'congress', loadComponent: () => import('./pages/congress/congress').then(m => m.CongressPage) },
  { path: 'experts', loadComponent: () => import('./pages/experts/experts').then(m => m.ExpertsPage) },
  { path: 'resources', loadComponent: () => import('./pages/resources/resources').then(m => m.ResourcesPage) },
  { path: 'about', loadComponent: () => import('./pages/about/about').then(m => m.AboutPage) },
  { path: '**', redirectTo: '' },
];
