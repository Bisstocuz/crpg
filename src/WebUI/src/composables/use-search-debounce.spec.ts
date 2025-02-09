import { useSearchDebounced } from './use-search-debounce'

const { mockedPush, mockedUseRoute } = vi.hoisted(() => ({
  mockedPush: vi.fn(),
  mockedUseRoute: vi.fn(),
}))
vi.mock('vue-router', () => ({
  useRoute: mockedUseRoute,
  useRouter: vi.fn().mockImplementation(() => ({
    push: mockedPush,
  })),
}))

vi.mock('@vueuse/core', () => ({
  useDebounceFn: vi.fn(fn => fn),
}))

describe('searchModel', () => {
  it('empty query', () => {
    mockedUseRoute.mockImplementation(() => ({
      query: {},
    }))

    const { searchModel } = useSearchDebounced()

    expect(searchModel.value).toEqual('')
  })

  it('with query', () => {
    mockedUseRoute.mockImplementation(() => ({
      query: {
        search: 'mlp',
      },
    }))

    const { searchModel } = useSearchDebounced()

    expect(searchModel.value).toEqual('mlp')
  })

  it('change', () => {
    mockedUseRoute.mockImplementation(() => ({
      query: {
        search: 'mlp',
      },
    }))

    const { searchModel } = useSearchDebounced()

    searchModel.value = 'mlp the best'

    expect(mockedPush).toBeCalledWith({
      query: {
        search: 'mlp the best',
      },
    })
  })
})
