import { signIn } from "../sign-in";

export async function POST(request: Request) {
  return signIn("/api/v1/auth/login", await request.json());
}
