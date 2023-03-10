use std;
use add_one;

pub fn add_two(a: i32) -> i32 {
    a + 2
}

#[cfg(test)]
mod tests1 {
    use super::*;
    use add_one::*;

    #[test]
    fn it_works_passing2() {
        assert_eq!(3, add_two(1));
    }

    #[test]
    fn it_works_failing2() {
        assert_eq!(3, add_two(4));
    }

    #[test]
    #[ignore = "this is a test for ignore aka skip"]
    fn it_works_skipped2() {
        assert_eq!(3, add_two(2));
    }
}
