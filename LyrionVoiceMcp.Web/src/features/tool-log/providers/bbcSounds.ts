import { array, object, string } from '../../../shared/api/decoder';

export const providerSearchFields = {
  bbcSoundsSubscribed: array(object({ title: string, browseRef: string }))
};

export function providerSearchGroups(result: { bbcSoundsSubscribed: { title: string; browseRef: string }[] }) {
  return [{ title: 'BBC Sounds subscriptions', items: result.bbcSoundsSubscribed }];
}
