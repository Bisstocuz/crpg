import { createTestingPinia } from '@pinia/testing'
import { flushPromises } from '@vue/test-utils'

import type { Clan, ClanWithMemberCount } from '~/models/clan'

import { mountWithRouter } from '~/__test__/unit/utils'
import { Region } from '~/models/region'
import { useUserStore } from '~/stores/user'

import Page from './index.vue'

const { mockedUsePagination } = vi.hoisted(() => ({
  mockedUsePagination: vi.fn().mockImplementation(() => ({
    perPage: 2,
    perPageConfig: [1, 2, 3],
  })),
}))
vi.mock('~/composables/use-pagination', () => ({
  usePagination: mockedUsePagination,
}))

const { mockedUseSearchDebounced } = vi.hoisted(() => ({
  mockedUseSearchDebounced: vi.fn().mockImplementation(() => ({
    searchModel: ref(''),
  })),
}))
vi.mock('~/composables/use-search-debounce', () => ({
  useSearchDebounced: mockedUseSearchDebounced,
}))

const { mockedGetClans, mockedGetFilteredClans } = vi.hoisted(() => ({
  mockedGetClans: vi.fn().mockResolvedValue([]),
  mockedGetFilteredClans: vi.fn().mockReturnValue([]),
}))
vi.mock('~/services/clan-service', () => ({
  getClans: mockedGetClans,
  getFilteredClans: mockedGetFilteredClans,
}))

const userStore = useUserStore(createTestingPinia())

const routes = [
  {
    component: Page,
    name: 'Clans',
    path: '/clans',
  },
  {
    component: {
      template: `<div></div>`,
    },
    name: 'ClansId',
    path: '/:id',
  },
]
const route = {
  name: 'Clans',
  query: {},
}

const mountOptions = {
  global: {
    renderStubDefaultSlot: true,
    stubs: {
      ClanTagIcon: true,
      ResultNotFound: true,
    },
  },
}

const PONY_CLAN_ID = 1
const PONY_CLAN = {
  clan: {
    id: PONY_CLAN_ID,
    name: 'My Little Pony',
    primaryColor: '#fff',
    region: Region.Eu,
    tag: 'MLP',
  },
  memberCount: 4,
} as ClanWithMemberCount<Clan>

const UNICORN_CLAN_ID = 2
const UNICORN_CLAN = {
  clan: {
    id: UNICORN_CLAN_ID,
    name: 'Unicorns are best',
    primaryColor: '#fff',
    region: Region.Na,
    tag: 'UNI',
  },
  memberCount: 3,
} as ClanWithMemberCount<Clan>

beforeEach(() => {
  userStore.$patch({
    user: {
      region: Region.Eu,
    },
  })
})

it('user haven`t a clan - createNewClanButton should be active', async () => {
  const { wrapper } = await mountWithRouter(mountOptions, routes, route)

  expect(wrapper.findComponent('[data-aq-create-clan-button]').exists()).toBeTruthy()
})

it('user have a clan - myClanButton should be active', async () => {
  userStore.$patch({ clan: { id: PONY_CLAN_ID } as Clan })
  mockedGetFilteredClans.mockReturnValue([PONY_CLAN, UNICORN_CLAN])

  const { wrapper } = await mountWithRouter(mountOptions, routes, route)

  expect(wrapper.findComponent('[data-aq-create-clan-button]').exists()).toBeFalsy()
  expect(wrapper.findComponent('[data-aq-my-clan-button]').exists()).toBeTruthy()

  // The user's clan is highlighted in the list
  const rows = wrapper.findAll('tr')

  // Ignore table head row + sorting rows by number of clan members
  expect(rows.at(1)!.classes('text-primary')).toBeTruthy()
  expect(rows.at(1)!.find('[data-aq-clan-row="self-clan"]').exists()).toBeTruthy()
})

it('pagination shown', async () => {
  mockedGetFilteredClans.mockReturnValue([PONY_CLAN, PONY_CLAN, PONY_CLAN])
  const { wrapper } = await mountWithRouter(mountOptions, routes, route)

  expect(wrapper.findComponent({ name: 'OPagination' }).exists()).toBeTruthy()
})

it('pagination hidden', async () => {
  mockedGetFilteredClans.mockReturnValue([PONY_CLAN, UNICORN_CLAN])
  const { wrapper } = await mountWithRouter(mountOptions, routes, route)

  expect(wrapper.findComponent({ name: 'OPagination' }).exists()).toBeFalsy()
})

it('click on row - should be redirect to clan detail page', async () => {
  mockedGetFilteredClans.mockReturnValue([PONY_CLAN])
  const { router, wrapper } = await mountWithRouter(mountOptions, routes, route)
  const spyRouter = vi.spyOn(router, 'push')

  const rows = wrapper.findAll('tr')
  await rows.at(1)!.trigger('click')

  expect(spyRouter).toBeCalledWith({
    name: 'ClansId',
    params: {
      id: 1,
    },
  })
})

it('change region - should be change query params', async () => {
  const { router, wrapper } = await mountWithRouter(mountOptions, routes, route)
  const spyRouter = vi.spyOn(router, 'replace')

  const regionNaTabButton = wrapper.find('[role="tab"][aria-controls="Na-content"] button')

  await regionNaTabButton.trigger('click')

  expect(spyRouter).toBeCalledWith({
    query: {
      region: 'Na',
    },
  })

  await flushPromises()

  expect(mockedGetFilteredClans).toHaveBeenNthCalledWith(1, [], 'Eu', '') // on setup
  expect(mockedGetFilteredClans).toHaveBeenNthCalledWith(2, [], 'Na', '')
})

it('search - should be exec getFilteredClans', async () => {
  const { wrapper } = await mountWithRouter(mountOptions, routes, route)

  const searchInput = wrapper.find('[data-aq-search-clan-input]')

  await searchInput.setValue('mlp')

  expect(mockedGetFilteredClans).toHaveBeenNthCalledWith(1, [], 'Eu', '') // on setup
  expect(mockedGetFilteredClans).toHaveBeenNthCalledWith(2, [], 'Eu', 'mlp')
})
