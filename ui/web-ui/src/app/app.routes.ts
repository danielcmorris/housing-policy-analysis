import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then(m => m.HomePage) },
  { path: 'studies', loadComponent: () => import('./pages/library/library').then(m => m.LibraryPage) },
  { path: 'studies/:ref', loadComponent: () => import('./pages/study/study').then(m => m.StudyPage) },
  { path: 'bills/:id', loadComponent: () => import('./pages/bill-review/bill-review').then(m => m.BillReviewPage) },
  { path: 'assistant', loadComponent: () => import('./pages/assistant/assistant').then(m => m.AssistantPage) },
  { path: 'assistant/how-it-works', loadComponent: () => import('./pages/assistant-about/assistant-about').then(m => m.AssistantAboutPage) },
  { path: 'commons', loadComponent: () => import('./pages/commons/commons').then(m => m.CommonsPage) },
  { path: 'congress', loadComponent: () => import('./pages/congress/congress').then(m => m.CongressPage) },
  { path: 'experts', loadComponent: () => import('./pages/experts/experts').then(m => m.ExpertsPage) },
  { path: 'experts/:slug', loadComponent: () => import('./pages/expert-profile/expert-profile').then(m => m.ExpertProfilePage) },
  { path: 'resources', loadComponent: () => import('./pages/resources/resources').then(m => m.ResourcesPage) },
  { path: 'about', loadComponent: () => import('./pages/about/about').then(m => m.AboutPage) },
  { path: 'admin/dashboard', loadComponent: () => import('./pages/admin-dashboard/admin-dashboard').then(m => m.AdminDashboardPage) },
  { path: 'admin/bills', loadComponent: () => import('./pages/admin-bills/admin-bills').then(m => m.AdminBillsPage) },
  { path: 'admin/studies', loadComponent: () => import('./pages/admin-studies/admin-studies').then(m => m.AdminStudiesPage) },
  { path: 'admin/experts', loadComponent: () => import('./pages/admin-experts/admin-experts').then(m => m.AdminExpertsPage) },
  { path: '**', redirectTo: '' },
];
