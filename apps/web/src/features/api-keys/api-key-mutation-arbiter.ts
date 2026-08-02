export type ApiKeyMutationLease = Readonly<{
  apiKeyId: string;
  generation: number;
}>;

export type ApiKeyMutationArbiter = Readonly<{
  acquire: (apiKeyId: string) => ApiKeyMutationLease | null;
  isCurrent: (lease: ApiKeyMutationLease) => boolean;
  release: (lease: ApiKeyMutationLease) => void;
}>;
