import signedEnvelope from "../data/install-beta.json";
import release from "../data/beta.json";
import { createHandler } from "./routes.mjs";

const fetch = createHandler(release, signedEnvelope);
export default { fetch } satisfies ExportedHandler<Env>;
