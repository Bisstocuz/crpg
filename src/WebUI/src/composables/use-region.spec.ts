import { createTestingPinia } from '@pinia/testing'

import { Region } from '~/models/region'
import { useUserStore } from '~/stores/user'

import { useRegion } from './use-region'

const { mockedPush, mockedUseRoute } = vi.hoisted(() => ({
  mockedPush: vi.fn(),
  mockedUseRoute: vi.fn(),
}))
vi.mock('virtual:vue-router/auto', () => ({
  useRoute: mockedUseRoute,
  useRouter: vi.fn().mockImplementation(() => ({
    push: mockedPush,
  })),
}))

const userStore = useUserStore(createTestingPinia())

it('empty query', () => {
  userStore.$patch({ user: { region: Region.Na } })

  mockedUseRoute.mockImplementation(() => ({
    query: {},
  }))

  const { regionModel } = useRegion()

  expect(regionModel.value).toEqual(Region.Na)
})

it('with query', () => {
  userStore.$patch({ user: { region: Region.Na } })

  mockedUseRoute.mockImplementation(() => ({
    query: {
      region: Region.As,
    },
  }))

  const { regionModel } = useRegion()

  expect(regionModel.value).toEqual(Region.As)
})

it('default value', () => {
  userStore.$patch({ user: { region: Region.Eu } })

  mockedUseRoute.mockImplementation(() => ({
    query: {},
  }))

  const { regionModel } = useRegion()

  expect(regionModel.value).toEqual(Region.Eu)
})

it('regions list', () => {
  userStore.$patch({ user: { region: Region.Eu } })

  mockedUseRoute.mockImplementation(() => ({
    query: {},
  }))

  const { regions } = useRegion()

  expect(regions).toEqual(Object.keys(Region))
})
