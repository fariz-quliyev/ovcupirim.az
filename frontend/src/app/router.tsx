import { createBrowserRouter } from 'react-router'

import { ProtectedRoute } from '@/features/auth/ProtectedRoute'
import { AdminLayout } from '@/layouts/AdminLayout'
import { PublicLayout } from '@/layouts/PublicLayout'
import { AdminAuditPage } from '@/pages/admin/AdminAuditPage'
import { AdminLoginPage } from '@/pages/admin/AdminLoginPage'
import { AdminPasswordPage } from '@/pages/admin/AdminPasswordPage'
import { AdminPackagesPage } from '@/pages/admin/AdminPackagesPage'
import { AdminPaymentsPage } from '@/pages/admin/AdminPaymentsPage'
import { AdminDashboardPage } from '@/pages/admin/AdminDashboardPage'
import { AdminModerationPage } from '@/pages/admin/AdminModerationPage'
import { AdminRegionsPage } from '@/pages/admin/AdminRegionsPage'
import { AdminReportsPage } from '@/pages/admin/AdminReportsPage'
import { AdminStoresPage } from '@/pages/admin/AdminStoresPage'
import { AdminTaxonomyPage } from '@/pages/admin/AdminTaxonomyPage'
import { AdminUsersPage } from '@/pages/admin/AdminUsersPage'
import { AccountPage } from '@/pages/AccountPage'
import { CategoriesPage } from '@/pages/CategoriesPage'
import { CategoryBrowsePage } from '@/pages/CategoryBrowsePage'
import { CataloguePage } from '@/pages/CataloguePage'
import { CreateListingPage } from '@/pages/CreateListingPage'
import { EditListingPage } from '@/pages/EditListingPage'
import { FavoritesPage } from '@/pages/FavoritesPage'
import { GuidePage } from '@/pages/GuidePage'
import { HelpPage } from '@/pages/HelpPage'
import { HomePage } from '@/pages/HomePage'
import { ListingDetailPage } from '@/pages/ListingDetailPage'
import { LoginPage } from '@/pages/LoginPage'
import { MyListingsPage } from '@/pages/MyListingsPage'
import { MyPaymentsPage } from '@/pages/MyPaymentsPage'
import { MyStorePage } from '@/pages/MyStorePage'
import { NotificationsPage } from '@/pages/NotificationsPage'
import { SearchPage } from '@/pages/SearchPage'
import { NotFoundPage } from '@/pages/NotFoundPage'
import { PlaceholderPage } from '@/pages/PlaceholderPage'
import { PromotionReturnPage } from '@/pages/PromotionReturnPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { StaticPageView } from '@/pages/StaticPageView'
import { StorePage } from '@/pages/StorePage'
import { StoresPage } from '@/pages/StoresPage'

/**
 * Route inventory from plan §11. Pages land phase by phase, but every route already
 * owns its final URL so links, SEO and deep linking never have to be reworked.
 */
export const router = createBrowserRouter([
  {
    path: '/',
    element: <PublicLayout />,
    children: [
      { index: true, element: <HomePage /> },

      { path: 'elanlar', element: <CataloguePage /> },
      { path: 'elanlar/:categorySlug', element: <CataloguePage /> },
      { path: 'elanlar/:categorySlug/:subSlug', element: <CataloguePage /> },
      { path: 'axtaris', element: <SearchPage /> },
      { path: 'elan/:slug', element: <ListingDetailPage /> },


      { path: 'magazalar', element: <StoresPage /> },
      { path: 'magaza/:slug', element: <StorePage /> },
      { path: 'xerite', element: <PlaceholderPage title="Xəritə" phase="Phase 6 — Kəşf və istifadəçi" /> },

      { path: 'giris', element: <LoginPage /> },
      { path: 'qeydiyyat', element: <RegisterPage /> },

      // Everything below the guard needs a signed-in user; the API enforces the same rules.
      {
        element: <ProtectedRoute />,
        children: [
          { path: 'kabinet', element: <AccountPage /> },
          { path: 'yeni-elan', element: <CreateListingPage /> },
          { path: 'kabinet/elanlarim', element: <MyListingsPage /> },
          { path: 'kabinet/magazam', element: <MyStorePage /> },
          { path: 'kabinet/bildirisler', element: <NotificationsPage /> },
          { path: 'kabinet/odenisler', element: <MyPaymentsPage /> },
          { path: 'kabinet/elanlarim/:id/duzelis', element: <EditListingPage /> },
          { path: 'secilmisler', element: <FavoritesPage /> },
          { path: 'promotions/orders/:id/return', element: <PromotionReturnPage /> },
        ],
      },

      { path: 'kateqoriyalar', element: <CategoriesPage /> },
      { path: 'kateqoriyalar/:categorySlug', element: <CategoryBrowsePage /> },

      { path: 'melumat/:slug', element: <StaticPageView /> },
      { path: 'yardim', element: <HelpPage /> },
      { path: 'beledci', element: <GuidePage /> },
      { path: 'beledci/:slug', element: <StaticPageView /> },
      { path: 'reklam', element: <StaticPageView slug="reklam-yerlesdirin" /> },

      { path: '*', element: <NotFoundPage /> },
    ],
  },

  /*
   * The operations panel. English path segments (PD-7.1) because it is an internal tool outside the
   * public Azerbaijani namespace; the interface itself is Azerbaijani like the rest of the site.
   *
   * The role guard here keeps a moderator out of screens they cannot use — it is not what protects
   * the data. Every endpoint behind these routes enforces its own policy and answers 403 to a
   * moderator who types an admin-only URL directly.
   */
  // Outside the guard, and outside the public layout: the operator sign-in screen has to be
  // reachable by someone who is not signed in, which is the whole point of it.
  { path: '/admin/giris', element: <AdminLoginPage /> },
  {
    path: '/admin',
    element: <ProtectedRoute roles={['Admin', 'Moderator']} signInPath="/admin/giris" />,
    children: [
      {
        element: <AdminLayout />,
        children: [
          { index: true, element: <AdminDashboardPage /> },
          { path: 'moderation', element: <AdminModerationPage /> },
          { path: 'reports', element: <AdminReportsPage /> },

          // Admin-only sections. The sidebar hides them from a moderator; the API refuses them.
          {
            element: <ProtectedRoute roles={['Admin']} />,
            children: [
              { path: 'stores', element: <AdminStoresPage /> },
              { path: 'taxonomy', element: <AdminTaxonomyPage /> },
              { path: 'regions', element: <AdminRegionsPage /> },
              { path: 'users', element: <AdminUsersPage /> },
              { path: 'audit', element: <AdminAuditPage /> },
              { path: 'payments', element: <AdminPaymentsPage /> },
              { path: 'packages', element: <AdminPackagesPage /> },
              { path: 'parol', element: <AdminPasswordPage /> },
            ],
          },

          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
])
